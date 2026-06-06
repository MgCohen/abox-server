[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Prompt,
    [Parameter(Mandatory)] [string]$Project,
    [Parameter(Mandatory)] [string]$OutFile,
    [string]$DirectiveFile,
    [string]$SessionId,
    [switch]$Resume
)

$ErrorActionPreference = 'Stop'
$errFile = "$OutFile.err"

$cliArgs = @('-p', $Prompt, '--output-format', 'json')
if ($Resume) {
    $cliArgs += @('--resume', $SessionId)
}
else {
    $cliArgs += @('--session-id', $SessionId, '--permission-mode', 'acceptEdits')
    if ($DirectiveFile) {
        $directive = Get-Content -Raw -LiteralPath $DirectiveFile
        $cliArgs += @('--append-system-prompt', $directive)
    }
}

Push-Location -LiteralPath $Project
try {
    # Empty stdin so -p never blocks waiting on a tty.
    '' | & claude @cliArgs 2> $errFile | Out-File -FilePath $OutFile -Encoding utf8
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
