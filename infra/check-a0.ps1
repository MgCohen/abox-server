#!/usr/bin/env pwsh
# Phase A0 verification — re-runnable on laptop or VM.
# Hard gate per PLANS/unity-agent-infrastructure.md §7 / executor block.
# Exits 0 if all checks pass, non-zero on first failure.

$ErrorActionPreference = 'Stop'
$fail = 0

function Pass($msg) { Write-Host "[PASS] $msg" -ForegroundColor Green }
function Fail($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; $script:fail++ }
function Info($msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }

# ---- Check 1: claude auth status reports Max + firstParty ----
try {
    $authJson = claude auth status 2>&1 | Out-String
    $auth = $authJson | ConvertFrom-Json
    if ($auth.loggedIn -and $auth.subscriptionType -eq 'max' -and $auth.apiProvider -eq 'firstParty') {
        Pass "claude auth: subscription=max, apiProvider=firstParty, account=$($auth.email)"
    } else {
        Fail "claude auth status returned unexpected shape: $authJson"
    }
} catch {
    Fail "claude auth status failed: $_"
}

# ---- Check 2: ANTHROPIC_API_KEY is unset or empty (value-based) ----
# The Claude Code harness injects ANTHROPIC_API_KEY="" into its own subprocess env
# as a safety override. That's harmless. A non-empty value would silently force API billing.
$apiKey = $env:ANTHROPIC_API_KEY
if ([string]::IsNullOrEmpty($apiKey)) {
    Pass "ANTHROPIC_API_KEY is unset or empty (no API-billing override)"
} else {
    Fail "ANTHROPIC_API_KEY is set to a non-empty value (length $($apiKey.Length)). This silently overrides OAuth. Unset it before continuing."
}

# Also surface the base URL for awareness, but don't gate on it.
if ($env:ANTHROPIC_BASE_URL) {
    Info "ANTHROPIC_BASE_URL=$($env:ANTHROPIC_BASE_URL) (informational)"
}

# ---- Check 3: Unity Personal eligibility ----
# Not scriptable — it's a legal/revenue question. Surface as a manual gate.
Info "Unity Personal EULA eligibility is a manual check (revenue + funding < ~`$200K USD/12mo). Not verified by this script."

if ($fail -gt 0) {
    Write-Host ""
    Write-Host "A0 GATE FAILED: $fail check(s) did not pass. Do not proceed to A1." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "A0 scriptable checks passed. Confirm Unity Personal eligibility manually before A1." -ForegroundColor Green
exit 0
