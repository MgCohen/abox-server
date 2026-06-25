# The codex agent box (ADR 0013 + ADR 0007): runs `codex exec` for one turn, driven from
# the host over a non-interactive `docker exec -i` pipe (codex needs no PTY — unlike claude).
# Nothing secret is baked in — the subscription auth.json arrives per turn on the mounted
# /home/box HOME (.codex/auth.json), the worktree on /work. Built as `abox-codex:latest`
# (see CodexBox.Image). The box is the wall, so codex runs with its own sandbox bypassed.
#
# .NET SDK base because the agent works a .NET worktree; node is codex's install runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
 && apt-get install -y --no-install-recommends nodejs npm git ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# codex CLI. Provisional: pinned to the host's version (0.134.0); confirm the in-box
# `codex --version` during live validation and bump deliberately.
RUN npm install -g @openai/codex@0.134.0

# Non-root: `app` is the base image's stock non-root user; the orchestrator additionally
# runs the box under its own uid:gid (docker --user) so the bind mounts are writable.
# HOME is the mounted /home/box at run time, carrying .codex/auth.json.
USER app
WORKDIR /work
ENTRYPOINT ["/bin/sh"]
