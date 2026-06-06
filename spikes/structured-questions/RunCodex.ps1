[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Prompt,
    [Parameter(Mandatory)] [string]$Project,
    [Parameter(Mandatory)] [string]$LastFile,
    [Parameter(Mandatory)] [string]$EventsFile,
    [string]$DirectiveFile,
    [string]$ResumeId
)

$ErrorActionPreference = 'Stop'
$errFile = "$EventsFile.err"

if ($ResumeId) {
    # `codex exec resume` rejects --cd and -s; cwd comes from the session, so we
    # Push-Location into the sandbox, and disable sandboxing the resume-compatible
    # way (--dangerously-bypass-approvals-and-sandbox).
    $stdin = $Prompt
    $cliArgs = @('exec', 'resume', $ResumeId, '-o', $LastFile,
        '--skip-git-repo-check', '--dangerously-bypass-approvals-and-sandbox',
        '--model', 'gpt-5.5', '--json', '-')
}
else {
    $directive = if ($DirectiveFile) { Get-Content -Raw -LiteralPath $DirectiveFile } else { '' }
    $stdin = "$directive`n`n$Prompt"
    $cliArgs = @('exec', '--cd', $Project, '-o', $LastFile,
        '--skip-git-repo-check', '-s', 'danger-full-access', '--model', 'gpt-5.5', '--json', '-')
}

Push-Location -LiteralPath $Project
try {
    $stdin | & codex @cliArgs 2> $errFile | Out-File -FilePath $EventsFile -Encoding utf8
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
