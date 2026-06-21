# The agent box (ADR 0013): runs `claude` for one turn, driven from the host over
# `docker exec -it`. Nothing secret is baked in — the per-turn sh hooks arrive on the
# /session mount, the worktree on /work, and the pre-onboarded credential on /home/box
# (HOME). Built as `abox-claude:latest` (see SandboxSettings.Image).
#
# .NET SDK base because the agent works a .NET worktree; node is claude's runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
 && apt-get install -y --no-install-recommends nodejs npm git ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# claude CLI. Provisional: pin the channel/version on the real host and confirm the
# in-box `claude --version` during the deferred credential validation (B1/B2).
RUN npm install -g @anthropic-ai/claude-code

# Non-root: bypassPermissions is refused as root (spike pty finding #3), and the box
# should never run the agent privileged. HOME is the mounted /home/box at run time.
RUN useradd --create-home --uid 1000 box
USER box
WORKDIR /work
ENTRYPOINT ["/bin/sh"]
