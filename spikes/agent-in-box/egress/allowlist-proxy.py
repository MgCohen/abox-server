#!/usr/bin/env python3
# Allowlist CONNECT proxy — the box's ONLY egress. Permits HTTPS CONNECT to a
# configured allowlist (default: api.anthropic.com), refuses everything else.
# The box is on an --internal docker network with no route out, so this proxy is
# the single exfil-relevant path; the allowlist is what keeps the in-box token safe.
import os, socket, sys, threading

ALLOW = {h.strip().lower() for h in os.environ.get("ALLOW", "api.anthropic.com").split(",") if h.strip()}
PORT = int(os.environ.get("PORT", "8888"))


def pipe(a, b):
    try:
        while True:
            d = a.recv(65536)
            if not d:
                break
            b.sendall(d)
    except OSError:
        pass
    finally:
        for s in (a, b):
            try:
                s.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass


def handle(c):
    try:
        c.settimeout(10)
        req = b""
        while b"\r\n" not in req:
            d = c.recv(4096)
            if not d:
                c.close(); return
            req += d
        parts = req.split(b"\r\n", 1)[0].decode("latin1").split()
        if len(parts) < 2 or parts[0].upper() != "CONNECT":
            c.sendall(b"HTTP/1.1 405 Method Not Allowed\r\n\r\n"); c.close(); return
        hostport = parts[1]
        host = hostport.rsplit(":", 1)[0].lower()
        port = int(hostport.rsplit(":", 1)[1]) if ":" in hostport else 443
        if host not in ALLOW:
            sys.stderr.write(f"[proxy] DENY {host}:{port}\n"); sys.stderr.flush()
            c.sendall(b"HTTP/1.1 403 Forbidden\r\n\r\n"); c.close(); return
        try:
            up = socket.create_connection((host, port), timeout=10)
        except OSError:
            c.sendall(b"HTTP/1.1 502 Bad Gateway\r\n\r\n"); c.close(); return
        sys.stderr.write(f"[proxy] ALLOW {host}:{port}\n"); sys.stderr.flush()
        c.sendall(b"HTTP/1.1 200 Connection established\r\n\r\n")
        c.settimeout(None)
        threading.Thread(target=pipe, args=(c, up), daemon=True).start()
        pipe(up, c)
    except Exception:
        try:
            c.close()
        except OSError:
            pass


def main():
    s = socket.socket(); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("0.0.0.0", PORT)); s.listen(64)
    sys.stderr.write(f"[proxy] listening :{PORT} allow={sorted(ALLOW)}\n"); sys.stderr.flush()
    while True:
        c, _ = s.accept()
        threading.Thread(target=handle, args=(c,), daemon=True).start()


if __name__ == "__main__":
    main()
