# The agent box: .NET SDK (the compile gate, per #81) + python (the pty driver) +
# the claude CLI. No Unity install — engine DLLs are mounted/baked separately (#81).
# Provisional: pin the claude install method/version on the real host.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# claude CLI — install per the current distribution channel on the host.
# Left as an explicit step so the spike records exactly how the box gets it.
#   e.g. RUN curl -fsSL https://claude.ai/install.sh | sh   (confirm on the host)

COPY in-box/ /in-box/
RUN chmod +x /in-box/*.sh /in-box/drive-turn.py

# The agent never holds a credential or a git remote (SPIKE.md A1/A4).
# HOME points at the mounted session dir at run time (see host/run-spike.sh) so
# claude's ~/.claude/projects JSONL lands on the mount.
ENTRYPOINT ["/bin/sh"]
