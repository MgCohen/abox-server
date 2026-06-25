#!/usr/bin/env python3
# Allowlist CONNECT proxy — the agent box's ONLY egress (ADR 0013). Permits HTTPS
# CONNECT to a configured allowlist (default: api.anthropic.com), refuses everything
# else. The box runs on an --internal docker network with no route out, so this proxy
# is the single exfil-relevant path; the allowlist is what keeps the in-box credential
# safe. Ported from spike agent-in-box/egress (validated E1–E5).
import ipaddress, os, socket, sys, threading

ALLOW = {h.strip().lower() for h in os.environ.get("ALLOW", "api.anthropic.com,chatgpt.com").split(",") if h.strip()}
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
        host, _, rawport = hostport.rpartition(":")
        if not host:
            host, port = hostport, 443
        else:
            try:
                port = int(rawport)
            except ValueError:
                c.sendall(b"HTTP/1.1 400 Bad Request\r\n\r\n"); c.close(); return
        host = host.strip("[]").lower()
        # Allowlist is host + 443 only — no arbitrary ports on an allowed host.
        if host not in ALLOW or port != 443:
            sys.stderr.write(f"[proxy] DENY {host}:{port}\n"); sys.stderr.flush()
            c.sendall(b"HTTP/1.1 403 Forbidden\r\n\r\n"); c.close(); return
        # Resolve here (the box can't) and refuse non-public targets, so a poisoned record
        # for an allowed name can't tunnel into the host's internal/metadata network.
        try:
            addr = socket.getaddrinfo(host, port, type=socket.SOCK_STREAM)[0][4][0]
        except OSError:
            c.sendall(b"HTTP/1.1 502 Bad Gateway\r\n\r\n"); c.close(); return
        if not ipaddress.ip_address(addr).is_global:
            sys.stderr.write(f"[proxy] DENY {host}:{port} -> {addr} (non-public)\n"); sys.stderr.flush()
            c.sendall(b"HTTP/1.1 403 Forbidden\r\n\r\n"); c.close(); return
        try:
            up = socket.create_connection((addr, port), timeout=10)
        except OSError:
            c.sendall(b"HTTP/1.1 502 Bad Gateway\r\n\r\n"); c.close(); return
        sys.stderr.write(f"[proxy] ALLOW {host}:{port} -> {addr}\n"); sys.stderr.flush()
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
