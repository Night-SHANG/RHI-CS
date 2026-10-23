$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../tools/ci_performance.ps1') -FunctionsOnly
function Assert-Equal($Actual, $Expected, [string]$Message) {
    if ($Actual -ne $Expected) { throw "$Message Expected $Expected; actual $Actual." }
}
Assert-Equal (Get-Median @(100, 1, 3)) 3 'Odd median must not be an average or minimum.'
Assert-Equal (Get-Median @(1, 9, 3, 5)) 4 'Even median must average the middle samples.'
foreach ($invalid in @(@{ values = @() }, @{ values = @([double]::NaN) }, @{ values = @(-1) })) {
    $rejected = $false
    try { Get-Median $invalid.values | Out-Null } catch { $rejected = $true }
    Assert-Equal $rejected $true 'Invalid samples must fail rather than produce a misleading median.'
}
$trials = @()
foreach ($role in @('baseline', 'candidate')) {
    foreach ($trial in 0..3) {
        $seconds = if ($trial -eq 0) { 9999 } elseif ($role -eq 'baseline') { @(1, 2, 3)[$trial - 1] } else { @(5, 4, 3)[$trial - 1] }
        $stages = @{}
        foreach ($name in @('systemCopy', 'systemDelete', 'freshDeploy.preflight', 'freshDeploy.execute',
                'overwrite.preflight', 'overwrite.execute', 'noop.preflight', 'noop.execute', 'clear.preflight', 'clear.execute')) {
            $stages[$name] = @{ seconds = $seconds; cpuSeconds = 0; managedAllocatedBytes = 0
                processHandlesBefore = 1; processHandlesAfter = 1
                processIo = @{ readOperations = 1; writeOperations = 1; otherOperations = 1; readBytes = 1; writeBytes = 1; otherBytes = 1 } }
        }
        $trials += @{ role = $role; trial = $trial; warmup = $trial -eq 0; verified = $true; stages = $stages }
    }
}
$comparison = @(Get-Comparisons $trials 3)
Assert-Equal $comparison.Count 14 'All references, preflights, executions and totals must be reported.'
$copy = $comparison | Where-Object stage -eq 'systemCopy'
Assert-Equal $copy.baseline.seconds 2 'Warmup must be excluded.'
Assert-Equal $copy.candidate.seconds 4 'All measured candidate trials must contribute.'
Assert-Equal $copy.direction 'slower' 'Regressions must remain visible.'
Assert-Equal $copy.candidateChangePercent 100 'Positive percentages mean slower.'
$total = $comparison | Where-Object stage -eq 'freshDeploy.total'
Assert-Equal $total.baseline.seconds 4 'Combined total must sum phases per trial.'
Assert-Equal @(Get-Comparisons $trials[0..6] 3).Count 0 'Incomplete series must not report medians.'
$trials[-1].verified = $false
Assert-Equal @(Get-Comparisons $trials 3).Count 0 'Failed integrity must invalidate the comparison.'
Write-Host 'Performance report aggregation checks passed.'
