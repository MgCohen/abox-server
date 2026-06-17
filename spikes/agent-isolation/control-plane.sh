#!/usr/bin/env bash
# Trusted control-plane runner for the agent-isolation spike.
#
# This is the privileged side of the boundary. In this Linux container it runs
# as root; in the real A.Box it is the .NET host process (or a privileged
# sidecar) running as a dedicated OS principal. It owns the secret, the real
# clone with a remote, and the bot identity. The agent never sees any of it.
#
# Sourced by rung1.sh / rung2.sh as a library of phase functions.

set -euo pipefail

RUNTIME="${ABOX_SPIKE_RUNTIME:-/opt/abox-spike}"
CP_HOME="$RUNTIME/cp"                 # root:root 700 — the agent cannot traverse this
CP_SECRET="$CP_HOME/secret.token"     # 600 — the credential at rest
CP_BARE="$RUNTIME/remote.git"         # stands in for the GitHub remote
CP_CLONE="$CP_HOME/clone"             # the real clone (has origin + identity)
CP_BASE="$CP_HOME/base"               # base snapshot used to diff the agent's work
WORK="$RUNTIME/work"                  # the ONLY shared surface: files in / files out
WORKER="abox-worker"
SECRET_VALUE="ghp_FAKEdeadbeefSPIKEonly_DO_NOT_USE"
BOT_NAME="ABox-Agent"
BOT_EMAIL="294015314+ABox-Agent@users.noreply.github.com"
CP_PID_FILE="$RUNTIME/cp.pid"

log() { printf '  [cp] %s\n' "$*" >&2; }

cp_setup() {
  id -u "$WORKER" >/dev/null 2>&1 || useradd -m -s /bin/bash "$WORKER"
  rm -rf "$RUNTIME"
  mkdir -p "$CP_HOME"
  chmod 700 "$CP_HOME"

  printf '%s\n' "$SECRET_VALUE" > "$CP_SECRET"
  chmod 600 "$CP_SECRET"

  git init -q --bare "$CP_BARE"
  git -C "$CP_BARE" symbolic-ref HEAD refs/heads/main
  git clone -q "$CP_BARE" "$CP_CLONE"
  ( cd "$CP_CLONE"
    printf 'hello from base\n' > hello.txt
    git -c user.name="$BOT_NAME" -c user.email="$BOT_EMAIL" add hello.txt
    git -c user.name="$BOT_NAME" -c user.email="$BOT_EMAIL" \
      commit -q -m "base: seed hello.txt"
    git push -q origin HEAD:refs/heads/main )
  log "control-plane owns secret ($CP_SECRET, 0600) + clone with remote"
}

# The long-lived trusted process: holds the credential in its OWN process
# memory/env, exactly as the orchestrator would. The agent will try to read it.
cp_start_process() {
  env SECRET_TOKEN="$SECRET_VALUE" GH_TOKEN="$SECRET_VALUE" \
    bash -c 'trap "" USR1 TERM; while true; do sleep 1; done' &
  echo $! > "$CP_PID_FILE"
  log "control-plane process up (pid $(cat "$CP_PID_FILE"), secret held in its env)"
}

cp_stop_process() {
  [ -f "$CP_PID_FILE" ] || return 0
  kill "$(cat "$CP_PID_FILE")" 2>/dev/null || true
  rm -f "$CP_PID_FILE"
}

# Stage base files into the shared working dir as plain files: NO .git, NO
# remote, NO credential. Owned by the worker so it can edit; root (the control
# plane) reaches it anyway.
cp_seed() {
  rm -rf "$WORK"
  mkdir -p "$WORK"
  git -C "$CP_CLONE" archive HEAD | tar -x -C "$WORK"
  mkdir -p "$CP_BASE"
  cp -a "$WORK/." "$CP_BASE/"
  chown -R "$WORKER:$WORKER" "$WORK"
  chmod 700 "$WORK"
  log "seeded workdir (plain files, no .git) -> $WORK"
}

# Read the agent's files back, diff against base, commit the diff into the real
# clone AS THE BOT, and push. The op is deterministic and does not trust the
# content; downstream gates (review, CI, ruleset) are what make blind-commit safe.
cp_harvest() {
  local rung="$1" out="$2"
  if diff -rq "$CP_BASE" "$WORK" >/dev/null 2>&1; then
    printf 'A7|control plane commits agent diff as the bot|FAIL|no change to commit\n' >> "$out"
    return
  fi
  cp -rf "$WORK/." "$CP_CLONE/"
  ( cd "$CP_CLONE"
    git -c user.name="$BOT_NAME" -c user.email="$BOT_EMAIL" add -A
    GIT_AUTHOR_NAME="$BOT_NAME" GIT_AUTHOR_EMAIL="$BOT_EMAIL" \
    GIT_COMMITTER_NAME="$BOT_NAME" GIT_COMMITTER_EMAIL="$BOT_EMAIL" \
      git commit -q -m "agent (rung$rung): apply worker diff"
    git push -q origin HEAD:refs/heads/main )
  local author
  author=$(git -C "$CP_BARE" log -1 main --format='%an <%ae>')
  if [ "$author" = "$BOT_NAME <$BOT_EMAIL>" ]; then
    printf 'A7|control plane commits agent diff as the bot|PASS|landed on remote authored by %s\n' "$author" >> "$out"
  else
    printf 'A7|control plane commits agent diff as the bot|FAIL|unexpected author %s\n' "$author" >> "$out"
  fi
}

cp_teardown() {
  cp_stop_process
  rm -rf "$RUNTIME"
}

SPIKE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AGENT="$SPIKE_DIR/fake-agent.sh"
ATTACKS_DIR="$SPIKE_DIR/attacks"
RESULTS_DIR="$SPIKE_DIR/results"

# Spawn the agent as the worker principal. The ONLY difference between rungs is
# the wrapper here: rung 1 = scrubbed env under a separate OS user; rung 2 adds
# net + pid + mount namespaces (no egress, host processes invisible) — what a
# container gives, via the same kernel primitive, minus the daemon.
spawn_agent() {
  local rung="$1" pid; pid="$(cat "$CP_PID_FILE")"
  local -a envv=(/usr/bin/env -i
    HOME="/home/$WORKER" PATH=/usr/local/bin:/usr/bin:/bin
    TERM=xterm-256color LANG=C.UTF-8
    WORKDIR="$WORK" CP_PID="$pid"
    CP_HINTS="$CP_SECRET:$CP_CLONE/.git/config:/root/.gitconfig"
    RUNG="$rung" ATTACKS_DIR="$ATTACKS_DIR"
    bash "$AGENT")
  case "$rung" in
    1) runuser -u "$WORKER" -- "${envv[@]}" ;;
    2) unshare --net --pid --mount --fork --mount-proc -- \
         runuser -u "$WORKER" -- "${envv[@]}" ;;
  esac
}

declare -A REQ1=( [A1]=PASS [A2]=PASS [A3]=REACHED [A4]=PASS [A5]=PARTIAL [A6]=PASS [A7]=PASS )
declare -A REQ2=( [A1]=PASS [A2]=PASS [A3]=PASS    [A4]=PASS [A5]=PASS    [A6]=PASS [A7]=PASS )

evaluate() {
  local rung="$1" out="$2" id req act desc all_met=0
  local -n REQ="REQ$rung"
  printf '\n  attack matrix — rung %s\n' "$rung"
  printf '  %-3s %-9s %-9s %-3s %s\n' ID REQUIRED ACTUAL OK ATTACK
  printf '  %s\n' "-------------------------------------------------------------------"
  while IFS='|' read -r id desc act detail; do
    [ -n "$id" ] || continue
    req="${REQ[$id]:-?}"
    if [ "$act" = "$req" ]; then ok="OK"; else ok="XX"; all_met=1; fi
    printf '  %-3s %-9s %-9s %-3s %s\n' "$id" "$req" "$act" "$ok" "$desc"
  done < <(sort "$out")
  printf '  %s\n' "-------------------------------------------------------------------"
  if [ "$all_met" = 0 ]; then printf '  RESULT: rung %s GREEN — every row met its required result.\n' "$rung"
  else printf '  RESULT: rung %s has rows off their required result (see XX above).\n' "$rung"; fi
  return "$all_met"
}

cp_run_rung() {
  local rung="$1"
  local out="$RESULTS_DIR/rung${rung}.txt"
  mkdir -p "$RESULTS_DIR"; : > "$out"
  printf '== agent-isolation spike — rung %s ==\n' "$rung" >&2
  cp_setup
  cp_start_process
  cp_seed
  log "spawning adversarial agent (rung $rung)"
  spawn_agent "$rung" >> "$out" 2>/dev/null || true
  cp_harvest "$rung" "$out"
  cp_stop_process
  local rc=0; evaluate "$rung" "$out" || rc=$?
  cp_teardown
  return "$rc"
}
