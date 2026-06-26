#!/usr/bin/env python3
# Run INSIDE the box. Proves the egress posture that makes the in-box token safe:
# no direct route out (internal net), and only the allowlisted host reachable via
# the proxy. Mirrors SPIKE.md A3/A5 (egress block / exfil attempt), ported to the box.
import os, socket, sys

PROXY = os.environ.get("PROXY_HOST", "egress-proxy")
PPORT = int(os.environ.get("PROXY_PORT", "8888"))
results = []


def direct(ip, port, label):
    # expect BLOCKED: the box has no route off the --internal network.
    try:
        socket.create_connection((ip, port), timeout=4).close()
        reachable = True
    except OSError:
        reachable = False
    results.append((label, "PASS" if not reachable else "FAIL",
                    "blocked (no route)" if not reachable else "REACHABLE — exfil hole"))


def via_proxy(host, port, label, expect_allow):
    try:
        s = socket.create_connection((PROXY, PPORT), timeout=5)
        s.sendall(f"CONNECT {host}:{port} HTTP/1.1\r\nHost: {host}:{port}\r\n\r\n".encode())
        status = s.recv(256).decode("latin1").split("\r\n", 1)[0]
        s.close()
        allowed = "200" in status
    except OSError as e:
        allowed, status = False, str(e)
    results.append((label, "PASS" if allowed == expect_allow else "FAIL", status))


# Direct exfil attempts — all must be blocked (no route off the internal net).
direct("169.254.169.254", 80, "E1 direct cloud-metadata 169.254.169.254")
direct("10.255.255.1", 80, "E2 direct RFC1918 10.255.255.1")
direct("1.1.1.1", 443, "E3 direct public 1.1.1.1 (bypass proxy)")
# Via the proxy: only the allowlisted host is permitted.
via_proxy("api.anthropic.com", 443, "E4 proxy -> api.anthropic.com (allowlisted)", True)
via_proxy("example.com", 443, "E5 proxy -> example.com (NOT allowlisted)", False)

for label, verdict, detail in results:
    print(f"{verdict}  {label}  [{detail}]")
sys.exit(0 if all(v == "PASS" for _, v, _ in results) else 1)
