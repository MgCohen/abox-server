#requires -Version 7
<#
.SYNOPSIS
  Onboard a repo to the agent/owner identity model. Run as the OWNER (MgCohen),
  not the bot.

.DESCRIPTION
  Two halves make the guarantee "the agent can author, only the owner can land
  protected changes":
    1. Identity (machine-wide, one-time): the bot's token lives in the global
       Claude settings `env` block so every Claude session in every project acts
       as ABox-Agent, while your interactive shells / SourceTree stay you. See
       README.md in this folder.
    2. Per-repo enforcement (this script): add the bot as a write collaborator
       and apply the `protect-main` ruleset with an empty bypass list, so the
       bot cannot self-approve its own protected-path PRs.

.EXAMPLE
  ./new-project-bootstrap.ps1 -Repo MgCohen/my-new-repo
  ./new-project-bootstrap.ps1 -Repo MgCohen/my-new-repo -StatusChecks 'build-test (ubuntu-latest)','build-test (windows-latest)'
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $Repo,
  [string] $Bot = 'ABox-Agent',
  [string[]] $StatusChecks = @()
)

$ErrorActionPreference = 'Stop'

$me = gh api user --jq '.login'
if ($me -eq $Bot) {
  throw "You are signed in as the bot ($Bot). Run this as the OWNER so the bypass list stays empty and the bot can't self-grant."
}
Write-Host "Owner: $me  ->  onboarding $Repo (bot: $Bot)" -ForegroundColor Cyan

Write-Host "1/2 Adding $Bot as a write collaborator..." -ForegroundColor Cyan
gh api -X PUT "repos/$Repo/collaborators/$Bot" -f permission=push | Out-Null

$prRule = @{
  type = 'pull_request'
  parameters = @{
    required_approving_review_count    = 1
    dismiss_stale_reviews_on_push      = $true
    require_code_owner_review          = $true
    require_last_push_approval         = $true
    required_review_thread_resolution  = $false
    allowed_merge_methods              = @('merge', 'squash', 'rebase')
  }
}
$rules = @(
  @{ type = 'deletion' },
  @{ type = 'non_fast_forward' },
  $prRule
)
if ($StatusChecks.Count -gt 0) {
  $rules += @{
    type = 'required_status_checks'
    parameters = @{
      strict_required_status_checks_policy = $true
      do_not_enforce_on_create             = $false
      required_status_checks               = @($StatusChecks | ForEach-Object { @{ context = $_ } })
    }
  }
}

$ruleset = @{
  name          = 'protect-main'
  target        = 'branch'
  enforcement   = 'active'
  bypass_actors = @()
  conditions    = @{ ref_name = @{ include = @('~DEFAULT_BRANCH'); exclude = @() } }
  rules         = $rules
}

$existing = gh api "repos/$Repo/rulesets" --jq '.[] | select(.name=="protect-main") | .id'
$body = $ruleset | ConvertTo-Json -Depth 12
if ($existing) {
  Write-Host "2/2 Updating existing protect-main ruleset ($existing)..." -ForegroundColor Cyan
  $body | gh api -X PUT "repos/$Repo/rulesets/$existing" --input - | Out-Null
} else {
  Write-Host "2/2 Creating protect-main ruleset..." -ForegroundColor Cyan
  $body | gh api -X POST "repos/$Repo/rulesets" --input - | Out-Null
}

Write-Host "Done. $Repo now requires an owner-approved, code-owner-reviewed PR to land on the default branch." -ForegroundColor Green
Write-Host "Reminder: also commit a CODEOWNERS file so protected paths request your review." -ForegroundColor Yellow
