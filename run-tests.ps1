param(
    [ValidateSet('EditMode', 'PlayMode', 'All')]
    [string]$Mode = 'All',
    [string]$Filter = '',
    [int]$Timeout = 1200
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'TestProject'
$outDir = Join-Path $root 'TestResults'
New-Item -ItemType Directory -Force $outDir | Out-Null

$modes = if ($Mode -eq 'All') { @('EditMode', 'PlayMode') } else { @($Mode) }
$failedTotal = 0

foreach ($m in $modes) {
    $xml = Join-Path $outDir "results-$($m.ToLower()).xml"
    if (Test-Path $xml) { Remove-Item $xml }

    $cliArgs = @('test', $project, '--mode', $m, '--output', $xml, '--timeout', $Timeout, '--json')
    if ($Filter) { $cliArgs += @('--filter', $Filter) }
    & unity @cliArgs | Out-Null

    if (-not (Test-Path $xml)) {
        Write-Host "[$m] No results file. Check the Unity log for compile errors." -ForegroundColor Red
        $failedTotal++
        continue
    }

    $run = ([xml](Get-Content $xml -Raw)).'test-run'
    $color = if ([int]$run.failed -eq 0) { 'Green' } else { 'Red' }
    Write-Host "[$m] total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped)" -ForegroundColor $color

    foreach ($case in ([xml](Get-Content $xml -Raw)).SelectNodes("//test-case[@result='Failed']")) {
        Write-Host "  FAIL $($case.fullname)" -ForegroundColor Red
        Write-Host "    $($case.failure.message.InnerText.Trim())"
    }
    $failedTotal += [int]$run.failed
}

exit $failedTotal
