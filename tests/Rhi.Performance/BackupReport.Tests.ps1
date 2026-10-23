$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../tools/ci_backup_performance.ps1') -FunctionsOnly
function Assert-Equal($Actual, $Expected, [string]$Message) {
    if ($Actual -ne $Expected) { throw "$Message Expected $Expected; actual $Actual." }
}
function Assert-Rejected([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-Equal $rejected $true $Message
}
function New-Trials {
    $trials = @()
    foreach ($role in @('baseline', 'candidate')) {
        foreach ($trial in 0..3) {
            $seconds = if ($trial -eq 0) { 9999 } elseif ($role -eq 'baseline') { @(1, 2, 3)[$trial - 1] } else { @(5, 4, 3)[$trial - 1] }
            $stages = @{}
            foreach ($name in @('systemCopy', 'systemDelete', 'firstUnmanagedOverwrite.preflight',
                'firstUnmanagedOverwrite.execute', 'managedSmallUpdate.preflight', 'managedSmallUpdate.execute')) {
                $stages[$name] = @{ seconds = $seconds; cpuSeconds = 0; managedAllocatedBytes = 0
                    processHandlesBefore = 1; processHandlesAfter = 1
                    processIo = @{ readOperations = 1; writeOperations = 1; otherOperations = 1; readBytes = 1; writeBytes = 1; otherBytes = 1 } }
            }
            $capture = @{}
            foreach ($scenario in @('firstUnmanagedOverwrite', 'managedSmallUpdate')) {
                $sample = @{}
                foreach ($metric in (Get-CaptureMetrics)) { $sample[$metric] = 0 }
                if ($scenario -eq 'firstUnmanagedOverwrite') {
                    $sample.seconds = $seconds / 2; $sample.calls = 990; $sample.succeededCalls = 990
                    $sample.publishedBlobs = 990; $sample.flushCalls = 990; $sample.bytesWritten = 129761280
                }
                $capture[$scenario] = $sample
            }
            $trials += @{ role = $role; trial = $trial; warmup = $trial -eq 0; verified = $true; suite = 'backup'
                stages = $stages; capture = $capture; verifiedScenarios = @('firstUnmanagedOverwrite', 'managedSmallUpdate')
                restoredScenarios = @('firstUnmanagedOverwrite') }
        }
    }
    return $trials
}
$trials = @(New-Trials)
$comparison = @(Get-BackupComparisons $trials 3)
Assert-Equal $comparison.Count 10 'Both scenarios and capture phases must be present.'
$copy = $comparison | Where-Object stage -eq 'systemCopy'
Assert-Equal $copy.baseline.seconds 2 'Warmup must be excluded.'
Assert-Equal $copy.candidate.seconds 4 'Every measured candidate trial must contribute.'
Assert-Equal $copy.direction 'slower' 'Regressions must remain visible.'
Assert-Equal $copy.candidateChangePercent 100 'Positive percentages mean slower.'
$total = $comparison | Where-Object stage -eq 'managedSmallUpdate.total'
Assert-Equal $total.baseline.seconds 4 'Total is preflight plus execute; never adds nested capture again.'
$backup = $comparison | Where-Object stage -eq 'firstUnmanagedOverwrite.capture'
Assert-Equal $backup.baseline.seconds 1 'Capture sums are measured separately.'
Assert-Equal $backup.baseline.calls 990 'Exact capture count must survive aggregation.'
Assert-Equal $backup.baseline.bytesWritten 129761280 'Exact logical bytes must survive aggregation.'
$noopCapture = $comparison | Where-Object stage -eq 'managedSmallUpdate.capture'
Assert-Equal $noopCapture.candidateChangePercent $null 'A zero baseline is not an infinite speedup.'
Assert-Equal $noopCapture.direction 'unchanged' 'Both zero captures must remain explicit.'
Assert-Equal @(Get-BackupComparisons $trials[0..6] 3).Count 0 'Incomplete series must not report medians.'
$trials[-1].verified = $false
Assert-Equal @(Get-BackupComparisons $trials 3).Count 0 'A failed integrity oracle invalidates comparison.'
$trials = @(New-Trials); $trials[-1].stages.Remove('managedSmallUpdate.execute')
Assert-Rejected { Get-BackupComparisons $trials 3 } 'A missing phase must not silently convert to zero.'
$trials = @(New-Trials); $trials[-1].capture.firstUnmanagedOverwrite.Remove('bytesHashed')
Assert-Rejected { Get-BackupComparisons $trials 3 } 'A missing exact counter must fail.'
$trials = @(New-Trials); $trials[-1].capture.firstUnmanagedOverwrite.seconds = [double]::NaN
Assert-Rejected { Get-BackupComparisons $trials 3 } 'Nonfinite metrics must fail.'
$trials = @(New-Trials); $trials[-1].restoredScenarios = @()
Assert-Rejected { Get-BackupComparisons $trials 3 } 'A missing restoration oracle must fail.'
$trials = @(New-Trials); $trials[-1].trial = 2
Assert-Rejected { Get-BackupComparisons $trials 3 } 'Duplicate measured trial numbers must fail.'
$trials = @(New-Trials); $trials[-1].suite = 'deployment'
Assert-Rejected { Get-BackupComparisons $trials 3 } 'A legacy suite must not be merged into backup results.'
Write-Host 'Backup performance report aggregation checks passed.'
