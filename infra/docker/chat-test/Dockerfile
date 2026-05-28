# Chat-layer pre-flight container (Plan §A3.5).
#
# Purpose: validate ttyd + tmux + claude rendering on mobile (phone over
# Tailscale) BEFORE provisioning a Hetzner VM. Catches xterm.js + Claude TUI
# rendering bugs on the laptop where iteration is free.
#
# NOT for Unity — Unity Personal cannot activate inside a container
# (see PLANS/unity-agent-infrastructure.md §11 "Unity Personal in containers").
# The VM will use Unity Hub native install, not this image.
#
# Build:
#   docker build -t chat-test C:/Unity/remote-unity-agents/infra/docker/chat-test
#
# Run (Windows PowerShell):
#   docker run -d --name chat-test `
#     -e CLAUDE_CODE_OAUTH_TOKEN=$env:CLAUDE_CODE_OAUTH_TOKEN `
#     -p 100.86.249.67:7681:7681 `
#     chat-test
#
# Then open http://100.86.249.67:7681 from laptop + phone (Tailscale).

FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive \
    LANG=C.UTF-8 \
    LC_ALL=C.UTF-8

# System packages.
#  - curl + ca-certificates: for claude installer
#  - git: claude expects git in PATH (project context)
#  - tmux: session multiplexer (the harness primitive)
#  - ttyd: browser-accessible TTY
#  - ripgrep: claude's preferred file-search backend
#  - tini: clean signal handling when tmux is PID 1
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates \
      curl \
      git \
      ripgrep \
      tini \
      tmux \
      ttyd \
    && rm -rf /var/lib/apt/lists/*

# Install official claude CLI (native installer, no Node dependency).
# Drops binary at /root/.local/bin/claude with background auto-update.
RUN curl -fsSL https://claude.ai/install.sh | bash
ENV PATH=/root/.local/bin:$PATH

# Print versions at build time for the build log.
RUN ttyd --version && tmux -V && claude --version

# Default working dir — claude will operate here unless overridden.
WORKDIR /work

EXPOSE 7681

# ttyd flag notes (Ubuntu 22.04 apt ships ttyd 1.6.3 — confirmed in build log):
#   -p 7681              — listen port
#   --ping-interval 30   — WS keepalive past idle proxies
#   --max-clients 2      — active + reconnecting; prevents fan-out
#
# NOTE on writable-by-default:
#   ttyd 1.6.3 is writable by default; --writable flag doesn't exist yet.
#   ttyd 1.7.4+ flipped to read-only-by-default; you'd need -W there.
#   We match the VM's `apt install ttyd` version for parity.
#
# tmux:
#   new-session -A -s main — attach if exists, else create. Idempotent.
#
# Final positional: `claude` is the program tmux runs inside the session.
ENTRYPOINT ["/usr/bin/tini", "--"]
CMD ["ttyd", \
     "-p", "7681", \
     "--ping-interval", "30", \
     "--max-clients", "2", \
     "tmux", "new-session", "-A", "-s", "main", "claude"]
