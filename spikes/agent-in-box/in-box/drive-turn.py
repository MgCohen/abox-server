#!/usr/bin/env python3
# Linux pty driver for one claude turn — a faithful port of the host's
# ClaudeProvider/ClaudeProtocol choreography (Windows ConPTY) to a Linux pty.
# Provisional: the TUI markers/keys are lifted from ClaudeProtocol.cs and may
# need tuning against the real CLI on the Docker host. That tuning IS the spike.
#
# usage: drive-turn.py "<prompt>" [bypass|default]
#
# Reads back nothing itself: the Stop hook writes the final message to
# $RA_STOP_SIGNAL and claude writes the JSONL under $HOME/.claude — both on the
# mounted session dir, so the host reads them off the mount.

import fcntl
import os
import pty
import re
import select
import struct
import termios
import sys
import time

ANSI = re.compile(rb"\x1b\[[0-9;?]*[ -/]*[@-~]|\x1b[@-Z\\-_]|[\x00-\x08\x0b-\x1f\x7f]")
WS = re.compile(rb"\s+")

# ClaudeProtocol.PromptReadyMarkers (whitespace-stripped match).
READY = (b"shift+tab", b"forshortcuts")
# ClaudeProtocol.DetectStartupDialog needles → keys (DialogKeys).
TRUST = (b"trustthisfolder", b"isthisaprojectyou")
BYPASS = (b"BypassPermissionsmode", b"Yes,Iaccept")
# First-run dialogs a FRESH config shows that a configured host never does (spike
# finding on Linux). Real product should pre-seed config; the driver dismisses them
# defensively by accepting the highlighted default (Enter).
THEME = (b"Choosethetextstyle", b"Let'sgetstarted")
SECURITY_NOTES = (b"Security notes", b"Pressentertocontinue", b"Yes,Itrust")

STARTUP_CAP = 30.0     # StartupCapMs
READY_SETTLE = 1.2     # ReadySettleMs
SUBMIT_SETTLE = 0.5    # SubmitSettleMs (oracle A5: anti-paste pause)
RESPONSE_CAP = 300.0   # ResponseCapMs
POLL = 0.15            # PollMs


def normalize(buf: bytes) -> bytes:
    return WS.sub(b"", ANSI.sub(b"", buf))


def main() -> int:
    prompt = sys.argv[1]
    mode = sys.argv[2] if len(sys.argv) > 2 else "bypass"
    perm_mode = "bypassPermissions" if mode == "bypass" else "default"
    session_id = os.environ.get("RA_SESSION_ID") or os.popen("cat /proc/sys/kernel/random/uuid").read().strip()
    signal_file = os.environ["RA_STOP_SIGNAL"]

    # ClaudeProtocol.BuildArgs (new session). Settings file wires the sh hooks.
    args = [
        "claude",
        "--session-id", session_id,
        "--permission-mode", perm_mode,
        "--settings", os.environ["RA_SETTINGS"],
    ]
    model = os.environ.get("RA_MODEL")
    if model:
        args += ["--model", model]

    print(f"[drive] user={os.getuid()} session={session_id} mode={perm_mode}", flush=True)

    # Subscription auth, fd-injection (mirrors the CCR host). The token arrives via a
    # tmpfs file the host mounts in — never a box-wide env var, so the gated-tool hook
    # children don't inherit it. We open it, mark the fd inheritable, and hand claude
    # CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR so the token stays out of environ.
    tok_path = os.environ.get("RA_OAUTH_TOKEN_FILE")
    tok_fd = None
    if tok_path and os.path.exists(tok_path):
        tok_fd = os.open(tok_path, os.O_RDONLY)
        os.set_inheritable(tok_fd, True)
        print(f"[drive] fd-injecting OAuth token from {tok_path} as fd {tok_fd}", flush=True)
    else:
        print("[drive] WARN: no RA_OAUTH_TOKEN_FILE — claude will hit Authentication error", flush=True)

    pid, fd = pty.fork()
    if pid == 0:
        if tok_fd is not None:
            os.environ["CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR"] = str(tok_fd)
        # Oracle A1: never leave an API key in the child env, or claude bills the API
        # instead of the subscription.
        os.environ.pop("ANTHROPIC_API_KEY", None)
        os.environ.pop("CLAUDE_API_KEY", None)
        os.execvp(args[0], args)
        os._exit(127)

    # The TUI won't draw its input bar at a 0x0 terminal — give the pty a real size
    # (ClaudeProvider uses 120x40). Without this the ready marker never appears.
    fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", 40, 120, 0, 0))

    buf = bytearray()

    def pump(seconds: float) -> None:
        end = time.time() + seconds
        while time.time() < end:
            r, _, _ = select.select([fd], [], [], POLL)
            if r:
                try:
                    buf.extend(os.read(fd, 65536))
                except OSError:
                    return

    # B2: the load-bearing check — claude must see an interactive terminal.
    print(f"[drive] isatty(pty)={os.isatty(fd)}", flush=True)

    # Dismiss each startup dialog at most once, until the input bar is ready.
    dismissed = set()
    deadline = time.time() + STARTUP_CAP
    while time.time() < deadline:
        pump(POLL)
        n = normalize(bytes(buf))
        if any(m in n for m in READY):
            break
        if any(k in n for k in BYPASS) and "bypass" not in dismissed:
            dismissed.add("bypass"); os.write(fd, b"2\r")
        elif any(k in n for k in TRUST) and "trust" not in dismissed:
            dismissed.add("trust"); os.write(fd, b"\r")
        elif any(k in n for k in THEME) and "theme" not in dismissed:
            dismissed.add("theme"); os.write(fd, b"\r")
        elif any(k in n for k in SECURITY_NOTES) and "secnotes" not in dismissed:
            dismissed.add("secnotes"); os.write(fd, b"\r")
    else:
        print("[drive] FAIL: input bar never became ready", flush=True)
        tail = ANSI.sub(b"", bytes(buf)).decode("latin1")[-1500:]
        print(f"[drive] --- last rendered screen (ansi-stripped, {len(buf)} bytes total) ---\n{tail}\n[drive] --- end ---", flush=True)
        return 1

    pump(READY_SETTLE)
    os.write(fd, prompt.encode())
    pump(SUBMIT_SETTLE)
    os.write(fd, b"\r")

    # Wait for the Stop hook to drop the signal file on the mounted session dir.
    end = time.time() + RESPONSE_CAP
    while time.time() < end:
        if os.path.exists(signal_file) and os.path.getsize(signal_file) > 0:
            print("[drive] Stop hook fired; final message + JSONL on the mount", flush=True)
            return 0
        pump(POLL)
    print("[drive] FAIL: Stop hook never fired within cap", flush=True)
    return 1


if __name__ == "__main__":
    sys.exit(main())
