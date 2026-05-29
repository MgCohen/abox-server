# hookshim.ps1 — RemoteAgents append shim for Claude / Codex hooks.
#
# Reads one hook event payload from stdin, wraps it with the source tag
# and a timestamp, and appends one compact JSON line to the path in
# $env:REMOTEAGENTS_HOOKS_JSONL. No-ops silently if the env var is unset
# (so the host's claude/codex keeps working if our settings.json is left
# behind after a crash).
#
# Usage:  pwsh -NoProfile -File hookshim.ps1 <source-tag>
#         (the orchestrator sets REMOTEAGENTS_HOOKS_JSONL on the agent
#          process; it propagates to hook commands via inheritance.)

param(
    [Parameter(Mandatory = $true)][string]$Source
)

$hooksPath = $env:REMOTEAGENTS_HOOKS_JSONL
if (-not $hooksPath) { exit 0 }

$stdin = [Console]::In.ReadToEnd()

try {
    $payload = $stdin | ConvertFrom-Json -AsHashtable -Depth 32
} catch {
    # Malformed payload — record it raw so the parser can skip the line
    # without losing forensic trail.
    $payload = @{ raw = $stdin }
}

$line = @{
    ts        = (Get-Date).ToUniversalTime().ToString("o")
    source    = $Source
    sessionId = $payload.session_id
    cwd       = $payload.cwd
    payload   = $payload
} | ConvertTo-Json -Depth 32 -Compress

# Add-Content is not strictly atomic against concurrent writers, but hook
# events within a single turn fire sequentially, so collisions are rare
# and malformed lines are tolerated (parser skips them).
Add-Content -Path $hooksPath -Value $line -Encoding UTF8
