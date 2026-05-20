#!/usr/bin/env pwsh
# Run Unity in batch-mode against a project path. Captures timing, exit code, log.
# Used by Phase A1 (local smoke test) and Phase A5 (VM smoke test).
#
# Args:
#   -ProjectPath <path>   Required. Worktree / project root containing Assets/ + ProjectSettings/.
#   -EditorVersion <ver>  Default: read from ProjectSettings/ProjectVersion.txt.
#   -LogDir <path>        Default: infra/logs/ (gitignored). Created if missing.
#   -Label <string>       Default: leaf dir of ProjectPath. Used in log filename.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,
    [string] $EditorVersion,
    [string] $LogDir,
    [string] $Label
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ProjectPath)) { throw "ProjectPath not found: $ProjectPath" }
$projVerFile = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
if (-not $EditorVersion) {
    if (-not (Test-Path $projVerFile)) { throw "ProjectVersion.txt not found and -EditorVersion not provided" }
    $EditorVersion = (Get-Content $projVerFile | Select-String '^m_EditorVersion:' | ForEach-Object { ($_ -split ':\s*')[1].Trim() }) | Select-Object -First 1
}

$unityExe = "C:\Program Files\Unity\Hub\Editor\$EditorVersion\Editor\Unity.exe"
if (-not (Test-Path $unityExe)) { throw "Unity Editor not found at $unityExe" }

if (-not $Label) { $Label = Split-Path $ProjectPath -Leaf }
if (-not $LogDir) { $LogDir = Join-Path $PSScriptRoot 'logs' }
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logFile = Join-Path $LogDir "$Label-$stamp.log"

$startTime = Get-Date
Write-Host "[$Label] Starting Unity $EditorVersion against $ProjectPath"
Write-Host "[$Label] Log: $logFile"

$proc = Start-Process -FilePath $unityExe -ArgumentList @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', $ProjectPath,
    '-logFile', $logFile
) -NoNewWindow -PassThru -Wait

$elapsed = (Get-Date) - $startTime
$libPath = Join-Path $ProjectPath 'Library'
$libSize = if (Test-Path $libPath) {
    [math]::Round(((Get-ChildItem $libPath -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Sum Length).Sum / 1GB), 2)
} else { 0 }
$lockfile = Join-Path $libPath 'UnityLockfile'
$lockPresent = Test-Path $lockfile

$result = [PSCustomObject]@{
    Label        = $Label
    ProjectPath  = $ProjectPath
    ExitCode     = $proc.ExitCode
    ElapsedSec   = [math]::Round($elapsed.TotalSeconds, 1)
    LibraryGB    = $libSize
    LockfileLeft = $lockPresent
    LogFile      = $logFile
}
$result | Format-List
$result | ConvertTo-Json -Compress | Write-Host
exit $proc.ExitCode
