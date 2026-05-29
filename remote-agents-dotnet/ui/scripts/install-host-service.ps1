# Install RemoteAgents.Host as a Windows service via nssm.
#
# Prereqs:
#   - nssm installed and on PATH (`choco install nssm` or grab the binary
#     from https://nssm.cc/download).
#   - Tailscale installed and signed in (`tailscale.exe` on PATH).
#   - .NET 10 SDK installed (the service runs `dotnet` directly).
#
# What it does:
#   1. Detects the Tailscale IPv4 address.
#   2. Publishes RemoteAgents.Host to ui/RemoteAgents.Host/publish/.
#   3. nssm install RemoteAgentsHost <published-exe>
#   4. Binds to http://<tailnet-ip>:5050 (no public exposure).
#   5. Enables AutoStart and restart-on-failure.
#
# Re-runnable: stops + reinstalls the service on each run.
#
# Run as Administrator (nssm needs it to register a service).

[CmdletBinding()]
param(
    [string] $ServiceName = "RemoteAgentsHost",
    [int]    $Port = 5050,
    [string] $RepoRoot = (Resolve-Path "$PSScriptRoot\..\..\..").Path,
    [switch] $SkipPublish
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script as Administrator (nssm requires it)."
}

# --- 1. Tailscale IP ---------------------------------------------------

$tailscale = Get-Command tailscale -ErrorAction SilentlyContinue
if (-not $tailscale) {
    $tailscale = Get-Command "C:\Program Files\Tailscale\tailscale.exe" -ErrorAction SilentlyContinue
}
if (-not $tailscale) { throw "tailscale.exe not found. Install Tailscale first." }

$tailnetIp = (& $tailscale.Source ip -4).Trim().Split("`n")[0].Trim()
if (-not $tailnetIp) { throw "tailscale ip -4 returned empty. Is Tailscale signed in?" }
Write-Host "Tailscale IP: $tailnetIp"

# --- 2. Publish --------------------------------------------------------

$hostProj = Join-Path $RepoRoot "remote-agents-dotnet\ui\RemoteAgents.Host\RemoteAgents.Host.csproj"
$publishDir = Join-Path $RepoRoot "remote-agents-dotnet\ui\RemoteAgents.Host\publish"

if (-not $SkipPublish) {
    Write-Host "Publishing $hostProj → $publishDir"
    dotnet publish $hostProj -c Release -o $publishDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}

$exe = Join-Path $publishDir "RemoteAgents.Host.exe"
if (-not (Test-Path $exe)) { throw "Expected $exe after publish, missing." }

# --- 3. nssm install (idempotent) --------------------------------------

$nssm = Get-Command nssm -ErrorAction SilentlyContinue
if (-not $nssm) { throw "nssm.exe not found. choco install nssm." }

$existing = & nssm status $ServiceName 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Service $ServiceName already exists, removing first..."
    & nssm stop $ServiceName confirm | Out-Null
    & nssm remove $ServiceName confirm | Out-Null
}

$bindUrl = "http://${tailnetIp}:${Port}"
Write-Host "Installing $ServiceName → $exe, binding $bindUrl"

& nssm install $ServiceName $exe
& nssm set $ServiceName AppDirectory $publishDir
& nssm set $ServiceName AppEnvironmentExtra "ASPNETCORE_URLS=$bindUrl" "ASPNETCORE_ENVIRONMENT=Production"
& nssm set $ServiceName Start SERVICE_AUTO_START
& nssm set $ServiceName AppExit Default Restart
& nssm set $ServiceName AppRestartDelay 5000
& nssm set $ServiceName AppStdout (Join-Path $publishDir "host.stdout.log")
& nssm set $ServiceName AppStderr (Join-Path $publishDir "host.stderr.log")
& nssm set $ServiceName AppRotateFiles 1
& nssm set $ServiceName AppRotateBytes 10485760

# --- 4. Tailscale dependency (best-effort) -----------------------------

# Tailscale service name varies by install path; this is a best-effort
# bind so that the host doesn't start before the tailnet interface is up.
$tsService = Get-Service "Tailscale" -ErrorAction SilentlyContinue
if ($tsService) {
    & nssm set $ServiceName DependOnService Tailscale | Out-Null
}

# --- 5. Start ---------------------------------------------------------

& nssm start $ServiceName
Write-Host ""
Write-Host "Service installed and started."
Write-Host "Verify:  curl $bindUrl/health"
Write-Host "Logs:    $publishDir\host.stdout.log"
