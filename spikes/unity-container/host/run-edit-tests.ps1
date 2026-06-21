# Rung 3 (U6): host closes the loop. After the in-container edit, run Unity
# edit-mode tests on the HOST over the SAME mounted project, proving Unity picks
# up the agent's change. Requires a licensed Unity 2022.3.50f1.
param(
  [string]$Project = "C:\Unity\random-game",
  [string]$Unity   = "C:\Program Files\Unity\Hub\Editor\2022.3.50f1\Editor\Unity.exe"
)
$ErrorActionPreference = "Stop"
$results = Join-Path $PSScriptRoot "..\results\rung3-editmode-results.xml"
$log     = Join-Path $PSScriptRoot "..\results\rung3-unity.log"

Write-Host "[rung3] Unity: $Unity"
Write-Host "[rung3] project: $Project"
Write-Host "[rung3] running edit-mode tests (batchmode, nographics)..."

# Unity.exe detaches on Windows; Start-Process -Wait blocks until it actually exits.
$args = @("-batchmode","-nographics","-projectPath",$Project,
          "-runTests","-testPlatform","EditMode",
          "-testResults",$results,"-logFile",$log)
$proc = Start-Process -FilePath $Unity -ArgumentList $args -Wait -PassThru -NoNewWindow
$code = $proc.ExitCode

Write-Host "[rung3] Unity exit code: $code"
if (Test-Path $results) {
  [xml]$xml = Get-Content $results
  $tc = $xml.SelectSingleNode("//test-run")
  if ($tc) { Write-Host "[rung3] total=$($tc.total) passed=$($tc.passed) failed=$($tc.failed) skipped=$($tc.skipped)" }
} else {
  Write-Host "[rung3] no results xml — check $log (licensing or compile failure)"
}
exit $code
