param(
    [string]$BaselineRoot,
    [string]$BaselineRevision = '76efd504ed0d3b473a5d4cc281cbf7d387fe2e32',
    [string]$CandidateRevision,
    [string]$OutputDirectory = 'artifacts/backup-performance',
    [ValidateRange(3, 3)][int]$TrialCount = 3,
    [switch]$FunctionsOnly
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
# Keep this coordinator independent of the historic script's parameter scope,
# expected-failure gate and fixed scenario list. Its worker protocol is unchanged.
function Get-Median([double[]]$Values) {
    if ($Values.Count -eq 0 -or @($Values | Where-Object { -not [double]::IsFinite($_) -or $_ -lt 0 }).Count -gt 0) {
        throw 'Median requires finite nonnegative samples.'
    }
    $sorted = @($Values | Sort-Object); $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($Values.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

function Read-WorkerResponse($Worker) {
    $read = $Worker.process.StandardOutput.ReadLineAsync()
    if (-not $read.Wait([TimeSpan]::FromMinutes(8))) { throw "$($Worker.role) worker response timed out." }
    $line = $read.Result
    if (-not $line) { throw "$($Worker.role) worker exited without a response. See worker stderr." }
    Add-Content -LiteralPath $Worker.stdout -Value $line -Encoding utf8
    return $line | ConvertFrom-Json -AsHashtable
}

function Start-Worker([string]$Role, [string]$Dll, [string]$Revision, [string]$Output) {
    $info = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in @($Dll, '--worker', (Join-Path $Output 'trials'), $Revision, $Role)) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    if (-not $process.Start()) { throw "Could not start $Role worker." }
    $worker = @{ role = $Role; process = $process; errorTask = $process.StandardError.ReadToEndAsync()
        stdout = Join-Path $Output "$Role-worker.stdout.log"; stderr = Join-Path $Output "$Role-worker.stderr.log" }
    $script:workers.Add($worker)
    $ready = Read-WorkerResponse $worker
    if (-not $ready.ready -or $ready.protocolVersion -ne 1) { throw "Invalid $Role worker handshake." }
    return $worker
}

function Get-CaptureMetrics {
    return @('seconds', 'calls', 'succeededCalls', 'sourceBytesRead', 'backupBytesRead', 'bytesWritten',
        'bytesHashed', 'hashPasses', 'flushCalls', 'publishedBlobs', 'reusedBlobs', 'writeThroughCalls', 'publishedCallsWithoutDurableFlush',
        'copySeconds', 'verificationSeconds', 'durableFlushSeconds', 'publishSeconds', 'unattributedSeconds')
}

function Get-CheckedMetric($Value, [string]$Metric, [string]$Context) {
    if ($null -eq $Value -or -not $Value.Contains($Metric) -or $null -eq $Value[$Metric]) {
        throw "Missing measurement: $Context/$Metric"
    }
    $number = [double]$Value[$Metric]
    if (-not [double]::IsFinite($number) -or ($number -lt 0 -and $Metric -ne 'unattributedSeconds')) {
        throw "Invalid measurement: $Context/$Metric"
    }
    return $number
}

function Get-BackupComparisons($Trials, [int]$Count) {
    $baseline = @($Trials | Where-Object { $_.role -eq 'baseline' -and -not $_.warmup -and $_.verified -and -not $_.error -and -not $_.cleanupError } | Sort-Object trial)
    $candidate = @($Trials | Where-Object { $_.role -eq 'candidate' -and -not $_.warmup -and $_.verified -and -not $_.error -and -not $_.cleanupError } | Sort-Object trial)
    if ($baseline.Count -ne $Count -or $candidate.Count -ne $Count) { return @() }
    foreach ($series in @(@{ items = $baseline }, @{ items = $candidate })) {
        if ((@($series.items.trial | Sort-Object -Unique) -join ',') -ne ((1..$Count) -join ',')) {
            throw 'Duplicate or missing measured trial number.'
        }
        foreach ($trial in $series.items) {
            if ($trial.suite -ne 'backup' -or $trial.verifiedScenarios -notcontains 'firstUnmanagedOverwrite' -or
                $trial.verifiedScenarios -notcontains 'managedSmallUpdate' -or
                $trial.restoredScenarios -notcontains 'firstUnmanagedOverwrite') {
                throw 'Backup comparison requires both verified scenarios and an untimed original-restoration check.'
            }
        }
    }
    $names = @('systemCopy', 'systemDelete', 'firstUnmanagedOverwrite.preflight', 'firstUnmanagedOverwrite.execute',
        'firstUnmanagedOverwrite.total', 'firstUnmanagedOverwrite.capture', 'managedSmallUpdate.preflight',
        'managedSmallUpdate.execute', 'managedSmallUpdate.total', 'managedSmallUpdate.capture')
    $comparisons = @()
    foreach ($name in $names) {
        $summaries = @{}
        foreach ($role in @('baseline', 'candidate')) {
            $series = if ($role -eq 'baseline') { $baseline } else { $candidate }
            $samples = @(); $measurements = @()
            foreach ($trial in $series) {
                if ($name.EndsWith('.total')) {
                    $scenario = $name.Substring(0, $name.Length - '.total'.Length)
                    $preflight = Get-CheckedMetric $trial.stages["$scenario.preflight"] 'seconds' "$role/$name/preflight"
                    $execute = Get-CheckedMetric $trial.stages["$scenario.execute"] 'seconds' "$role/$name/execute"
                    $samples += $preflight + $execute
                } elseif ($name.EndsWith('.capture')) {
                    $scenario = $name.Substring(0, $name.Length - '.capture'.Length)
                    $measurement = $trial.capture[$scenario]
                    foreach ($metric in (Get-CaptureMetrics)) { Get-CheckedMetric $measurement $metric "$role/$name" | Out-Null }
                    $measurements += $measurement; $samples += [double]$measurement.seconds
                } else {
                    $measurement = $trial.stages[$name]
                    $samples += Get-CheckedMetric $measurement 'seconds' "$role/$name"
                    $measurements += $measurement
                }
            }
            $summary = [ordered]@{ seconds = Get-Median $samples; sampleSeconds = $samples }
            if ($name.EndsWith('.capture')) {
                foreach ($metric in ((Get-CaptureMetrics) | Where-Object { $_ -ne 'seconds' })) {
                    # Unattributed can be a tiny signed rounding difference; retain
                    # it verbatim rather than clamping or treating it as speedup.
                    $values = @($measurements | ForEach-Object { [double]$_[$metric] })
                    $summary[$metric] = if ($metric -eq 'unattributedSeconds') {
                        @($values | Sort-Object)[[int][Math]::Floor($Count / 2)]
                    } else { Get-Median $values }
                }
            } elseif (-not $name.EndsWith('.total')) {
                foreach ($metric in @('cpuSeconds', 'managedAllocatedBytes', 'processHandlesBefore', 'processHandlesAfter')) {
                    $summary[$metric] = Get-Median @($measurements | ForEach-Object { Get-CheckedMetric $_ $metric "$role/$name" })
                }
                $io = [ordered]@{}
                foreach ($metric in @('readOperations', 'writeOperations', 'otherOperations', 'readBytes', 'writeBytes', 'otherBytes')) {
                    $io[$metric] = Get-Median @($measurements | ForEach-Object { Get-CheckedMetric $_.processIo $metric "$role/$name/processIo" })
                }
                $summary['processIo'] = $io
            }
            $summaries[$role] = $summary
        }
        $before = $summaries.baseline.seconds; $after = $summaries.candidate.seconds
        $comparisons += [ordered]@{
            stage = $name; baseline = $summaries.baseline; candidate = $summaries.candidate
            candidateMinusBaselineSeconds = $after - $before
            candidateChangePercent = if ($before -gt 0) { 100 * ($after - $before) / $before } else { $null }
            direction = if ($after -gt $before) { 'slower' } elseif ($after -lt $before) { 'faster' } else { 'unchanged' }
        }
    }
    return $comparisons
}

function Invoke-DeliveredIntegrityTests([string]$Root, [string]$Role, [string]$Output) {
    $results = Join-Path $Output "regression/$Role"
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    & dotnet test (Join-Path $Root 'tests/Rhi.Core.Tests/Rhi.Core.Tests.csproj') -c Release `
        --filter 'FullyQualifiedName~ExecutionIntegrityTests' --logger 'trx;LogFileName=integrity.trx' --results-directory $results `
        2>&1 | Tee-Object -FilePath (Join-Path $results 'test.log') | Out-Host
    $exitCode = $LASTEXITCODE
    $trxPath = Join-Path $results 'integrity.trx'
    if (-not (Test-Path -LiteralPath $trxPath)) { throw "$Role produced no TRX; compilation or discovery is not a test pass." }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $tests = @($trx.TestRun.Results.UnitTestResult | ForEach-Object {
        [ordered]@{ name = [string]$_.testName; outcome = [string]$_.outcome
            message = [string]$_.Output.ErrorInfo.Message; stackTrace = [string]$_.Output.ErrorInfo.StackTrace }
    })
    $expectedMethods = @('Target_edited_after_batch_validation_is_never_discarded',
        'Source_changed_after_batch_validation_is_not_published_or_receipted',
        'Copied_staging_bytes_remain_locked_until_publication',
        'Parked_original_is_locked_until_commit_and_cleanup_uses_its_handle',
        'Partial_copy_locks_source_and_releases_owned_handles_on_failure')
    foreach ($method in $expectedMethods) {
        $expectedCount = if ($method -eq $expectedMethods[0]) { 3 } else { 1 }
        if (@($tests | Where-Object { $_.name -match [regex]::Escape($method) }).Count -ne $expectedCount) {
            throw "$Role did not discover every expected case for $method."
        }
    }
    $result = [ordered]@{ role = $Role; expectedOutcome = 'allPassed'; exitCode = $exitCode
        total = [int]$counters.total; executed = [int]$counters.executed; passed = [int]$counters.passed
        failed = [int]$counters.failed; tests = $tests }
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $results 'result.json') -Encoding utf8
    if ($exitCode -ne 0 -or $result.total -ne 7 -or $result.executed -ne 7 -or $result.passed -ne 7 -or
        $result.failed -ne 0 -or $tests.Count -ne 7 -or @($tests | Where-Object outcome -ne 'Passed').Count -gt 0) {
        throw "$Role must pass all seven delivered integrity cases. This is not the old f26 expected-failure baseline."
    }
    return $result
}

function Install-MeasurementOnlyOverlay([string]$Baseline, [string]$SourceRoot) {
    $path = Join-Path $Baseline 'src/Rhi.Core/Storage/BlobStore.cs'
    $original = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($original))).ToLowerInvariant()
    if ($hash -ne '0b01267219cf171801737eb707df7a80ba748cbbb9b3ab234622aaae5ad8ff00') {
        throw 'Pinned baseline BlobStore differs from the reviewed measurement-only overlay input.'
    }
    $overlay = Join-Path $SourceRoot 'tests/Rhi.Performance/BaselineOverlay'
    $files = @()
    foreach ($name in @('BlobStore.cs', 'BlobCaptureMeasurement.cs')) {
        $source = Join-Path $overlay "$name.txt"
        $destination = Join-Path $Baseline "src/Rhi.Core/Storage/$name"
        Copy-Item -LiteralPath $source -Destination $destination
        $files += [ordered]@{ path = "src/Rhi.Core/Storage/$name"; sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    return [ordered]@{ kind = 'measurement-only'; originalBlobStoreLfSha256 = $hash; files = $files
        note = 'Original copy/flush/hash/move/verify sequence and sharing modes retained; adds observer, stopwatch and stream-position counters only. Timings include diagnostic overhead.' }
}

if ($FunctionsOnly) { return }
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $IsWindows -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Backup comparison runs only on a disposable GitHub-hosted Windows runner.'
}
if (-not $BaselineRoot -or -not $CandidateRevision) { throw 'BaselineRoot and CandidateRevision are required.' }
if ($BaselineRevision -ne '76efd504ed0d3b473a5d4cc281cbf7d387fe2e32') { throw 'The measurement overlay is reviewed only for delivered baseline 76efd50.' }
$root = Split-Path $PSScriptRoot -Parent
$BaselineRoot = (Resolve-Path -LiteralPath $BaselineRoot).Path
if ($BaselineRoot -eq $root) { throw 'Baseline and candidate require separate checkouts.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) { throw 'Output directory must be fresh; stale trials are not accepted.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$script:workers = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    schemaVersion = 1; suite = 'backup'; status = 'inProgress'; baselineRevision = $BaselineRevision; candidateRevision = $CandidateRevision
    runner = [ordered]@{ name = $env:RUNNER_NAME; environment = $env:RUNNER_ENVIRONMENT; image = $env:ImageOS
        imageVersion = $env:ImageVersion; processorCount = [Environment]::ProcessorCount }
    fixture = [ordered]@{ files = 990; bytesPerFile = 131072; payloadBytes = 129761280; executableBytes = 67108864
        managedChangedFiles = 10; managedUnchangedFiles = 980; measuredTrialsPerRevision = $TrialCount; warmupFiles = 32 }
    methodology = @(
        'Same current opt-in harness built separately against delivered 76efd50 plus a documented measurement-only overlay, and candidate Core. Both use the same observer schema.',
        'Delivered baseline and candidate must both pass all seven existing execution-integrity regressions. No expected baseline failure is assumed.',
        'Two persistent workers, one 32-file warmup each, then exactly three 990-file trials in B/C, C/B, B/C order on one Windows runner; never measured concurrently.',
        'firstUnmanagedOverwrite starts with 990 unmanaged original files and captures all originals. managedSmallUpdate first installs 990 files, then changes exactly ten deterministic source files with 980 unchanged.',
        'Fixture generation, initial install, V2 generation, source/target/backup/receipt verification, reporting, cleanup and post-overwrite Clear restoration are excluded from all timers.',
        'Capture seconds sums synchronous Capture call elapsed time nested inside execution. Never add Capture to execute or preflight+execute totals.',
        'Capture byte counters are actual logical stream-position counts, not physical disk I/O. HashPasses counts hash passes, not individual TransformBlock or read calls. FlushCalls counts explicit durable Flush(true) calls, not implicit write-through writes.',
        'Baseline CopySeconds is copy-only; candidate may combine inline hashing with copying. VerificationSeconds may group different checks. Compare total Capture seconds, not regrouped subphases as isolated algorithm speedups.',
        'No callback is attached during setup or restoration. Observer exceptions cannot change Capture success; exact fixture call-count assertions reject missing observations.',
        'Warm-cache/JIT-primed synthetic measurement, not cold-storage performance, physical-write volume, full application startup, user-PC prediction or statistical significance.',
        'Process I/O/CPU/allocations are stage-wide deltas; handles are endpoints, not peaks. JSON inventories are persisted lengths, not write-call counts. All slower trials and medians remain visible.',
        'File.Copy/File.Delete references lack transaction, journal, backup and explicit durable-flush guarantees and are not equivalent-safety alternatives.',
        'No UI build, artifact upload, release upload or external dispatch is performed. Only the documented disposable baseline source overlay is applied.'
    )
    overlay = $null; regressionSourceSha256 = $null; regressionGate = @(); executionOrder = @(); trials = @(); comparisons = @(); error = $null
}
function Save-BackupReport {
    $report.generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $report.trials = @(Get-ChildItem -LiteralPath (Join-Path $output 'trials') -Filter '*.json' -ErrorAction SilentlyContinue |
        Sort-Object Name | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -AsHashtable })
    $report.regressionGate = @(Get-ChildItem -LiteralPath (Join-Path $output 'regression') -Filter 'result.json' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -AsHashtable })
    $report.comparisons = @(Get-BackupComparisons $report.trials $TrialCount)
    $report | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath (Join-Path $output 'current.json') -Encoding utf8
}
Save-BackupReport
try {
    foreach ($entry in @(@{ root = $BaselineRoot; revision = $BaselineRevision }, @{ root = $root; revision = $CandidateRevision })) {
        $actual = & git -C $entry.root rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $entry.revision) { throw 'Checkout revision differs from requested comparison.' }
        $changes = & git -C $entry.root status --porcelain
        if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Both pinned checkouts must be clean before measurement; untracked source files are not allowed.' }
    }
    $report.overlay = Install-MeasurementOnlyOverlay $BaselineRoot $root
    Save-BackupReport
    $regression = 'tests/Rhi.Core.Tests/Deployment/ExecutionIntegrityTests.cs'
    $report.regressionSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $root $regression) -Algorithm SHA256).Hash.ToLowerInvariant()
    Copy-Item -LiteralPath (Join-Path $root $regression) -Destination (Join-Path $BaselineRoot $regression)
    Invoke-DeliveredIntegrityTests $BaselineRoot 'baseline' $output | Out-Null
    Save-BackupReport
    Invoke-DeliveredIntegrityTests $root 'candidate' $output | Out-Null
    Save-BackupReport
    $roles = @{}
    foreach ($role in @('baseline', 'candidate')) {
        $harness = Join-Path $output "harness/$role"; $build = Join-Path $output "build/$role"
        New-Item -ItemType Directory -Path $harness -Force | Out-Null
        Copy-Item -Path (Join-Path $root 'tests/Rhi.Performance/*.cs'), (Join-Path $root 'tests/Rhi.Performance/*.csproj') -Destination $harness
        Copy-Item -LiteralPath (Join-Path $root 'Directory.Build.props') -Destination $harness
        $coreRoot = if ($role -eq 'baseline') { $BaselineRoot } else { $root }
        $revision = if ($role -eq 'baseline') { $BaselineRevision } else { $CandidateRevision }
        $projectPath = Join-Path $harness 'Rhi.Performance.csproj'
        [xml]$project = Get-Content -LiteralPath $projectPath -Raw
        $project.Project.ItemGroup.ProjectReference.Include = Join-Path $coreRoot 'src/Rhi.Core/Rhi.Core.csproj'
        $project.Save($projectPath)
        & dotnet build $projectPath -c Release --output $build '-p:RhiCaptureMetrics=true' `
            2>&1 | Tee-Object -FilePath (Join-Path $output "$role-build.log") | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "$role backup harness build failed." }
        $roles[$role] = Start-Worker $role (Join-Path $build 'Rhi.Performance.dll') $revision $output
    }
    for ($trial = 0; $trial -le $TrialCount; $trial++) {
        $order = if ($trial -gt 0 -and $trial % 2 -eq 0) { @('candidate', 'baseline') } else { @('baseline', 'candidate') }
        foreach ($role in $order) {
            $report.executionOrder += [ordered]@{ role = $role; trial = $trial; warmup = $trial -eq 0 }
            Save-BackupReport
            Write-Host "$role backup $(if ($trial -eq 0) { 'warmup' } else { "trial $trial/$TrialCount" })"
            $roles[$role].process.StandardInput.WriteLine((@{ trial = $trial; warmup = $trial -eq 0; suite = 'backup' } | ConvertTo-Json -Compress))
            $roles[$role].process.StandardInput.Flush()
            $response = Read-WorkerResponse $roles[$role]
            Save-BackupReport
            if (-not $response.succeeded) { throw "$role trial $trial failed; partial measurements remain in current.json." }
            $last = $report.trials | Where-Object { $_.role -eq $role -and $_.trial -eq $trial }
            Write-Host ('BACKUP_TRIAL:' + ($last | ConvertTo-Json -Depth 30 -Compress))
        }
    }
    if (@($report.comparisons).Count -ne 10) { throw 'Incomplete paired backup results.' }
    $report.status = 'success'
} catch {
    $report.status = 'failed'; $report.error = $_.ToString()
    throw
} finally {
    foreach ($worker in $workers) {
        try {
            if (-not $worker.process.HasExited) {
                $worker.process.StandardInput.WriteLine('quit'); $worker.process.StandardInput.Close()
                if (-not $worker.process.WaitForExit(5000)) { $worker.process.Kill($true); $worker.process.WaitForExit() }
            }
            $worker.errorTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $worker.stderr -Encoding utf8
        } finally { $worker.process.Dispose() }
    }
    Save-BackupReport
    foreach ($comparison in $report.comparisons) {
        Write-Host ('BACKUP_SUMMARY:' + ($comparison | ConvertTo-Json -Depth 12 -Compress))
    }
    Write-Host ('BACKUP_REPORT:' + ($report | ConvertTo-Json -Depth 40 -Compress))
    if ($env:GITHUB_STEP_SUMMARY) {
        $summary = @('## Backup capture and managed small update', '', "Status: $($report.status)",
            "Baseline: $BaselineRevision (measurement-only overlay hashes in JSON)", "Candidate: $CandidateRevision", '',
            'Three paired 990-file trials, ten changed files for managedSmallUpdate. Positive change means slower.',
            'Capture is nested in execute, not an additional stage to add to the total. Restore verification is untimed.', '',
            '| Stage | Baseline median (s) | Candidate median (s) | Change |', '|---|---:|---:|---:|')
        foreach ($comparison in $report.comparisons) {
            $delta = if ($null -eq $comparison.candidateChangePercent) { 'n/a' } else { '{0:+0.00;-0.00;0.00}%' -f $comparison.candidateChangePercent }
            $summary += '| {0} | {1:F4} | {2:F4} | {3} |' -f $comparison.stage, $comparison.baseline.seconds, $comparison.candidate.seconds, $delta
        }
        if ($report.error) { $summary += @('', "Failure: $($report.error)") }
        $summary += @('', 'Exact Capture counters, all warmups/trials, overlay hashes and caveats are in BACKUP_REPORT console JSON. No artifact is uploaded.')
        $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
    }
}
