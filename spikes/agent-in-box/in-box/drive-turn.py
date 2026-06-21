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

import os
import pty
import re
import select
import sys
import time

ANSI = re.compile(rb"\x1b\[[0-9;?]*[ -/]*[@-~]|\x1b[@-Z\\-_]|[\x00-\x08\x0b-\x1f\x7f]")
WS = re.compile(rb"\s+")

# ClaudeProtocol.PromptReadyMarkers (whitespace-stripped match).
READY = (b"shift+tab", b"forshortcuts")
# ClaudeProtocol.DetectStartupDialog needles → keys (DialogKeys).
TRUST = (b"trustthisfolder", b"isthisaprojectyou")
BYPASS = (b"BypassPermissionsmode", b"Yes,Iaccept")

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

    pid, fd = pty.fork()
    if pid == 0:
        os.execvp(args[0], args)
        os._exit(127)

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
    else:
        print("[drive] FAIL: input bar never became ready", flush=True)
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
