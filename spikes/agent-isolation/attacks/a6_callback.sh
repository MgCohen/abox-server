#!/usr/bin/env bash
# A6 — call back into / introspect the control-plane process to lift its secret
# or force an action. PASS = its environ is unreadable (or the process is not
# even visible) and it cannot be signalled.
pid="${CP_PID:-0}"
read_env="denied"
if tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | grep -Eq 'TOKEN|SECRET'; then
  read_env="READ"
fi
if [ ! -e "/proc/$pid" ]; then
  read_env="not-visible"
fi
if [ "$read_env" = "READ" ]; then
  printf 'A6|introspect/signal the control plane|FAIL|read CP environ\n'
else
  printf 'A6|introspect/signal the control plane|PASS|CP environ %s\n' "$read_env"
fi
