# Configure Windows power settings so the laptop stays reachable while
# acting as the always-on Host. Run as Administrator.
#
# Touches only the AC profile (battery profile unchanged — laptop on
# battery still sleeps normally).

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run as Administrator."
}

# Never sleep on AC; turn monitor off after 30 min on AC.
powercfg /change standby-timeout-ac 0
powercfg /change hibernate-timeout-ac 0
powercfg /change monitor-timeout-ac 30
powercfg /change disk-timeout-ac 0

# Keep network adapter active during sleep (in case sleep ever does
# fire — defense in depth).
$adapter = Get-NetAdapterPowerManagement -Name "*" -ErrorAction SilentlyContinue |
    Where-Object { $_.AllowComputerToTurnOffDevice -eq "Enabled" }
foreach ($a in $adapter) {
    Set-NetAdapterPowerManagement -Name $a.Name -AllowComputerToTurnOffDevice Disabled
    Write-Host "Disabled power-off for adapter: $($a.Name)"
}

Write-Host ""
Write-Host "Power settings applied (AC profile)."
Write-Host "Verify:  powercfg /q"
