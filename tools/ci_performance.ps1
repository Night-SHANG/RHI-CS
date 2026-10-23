param(
    [string]$BaselineRoot,
    [string]$BaselineRevision = 'f26ed43614eac935fc4731073543bb17993544f5',
    [string]$CandidateRevision,
    [string]$OutputDirectory = 'artifacts/performance',
    [ValidateRange(3, 3)][int]$TrialCount = 3,
    [switch]$FunctionsOnly
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Get-Median([double[]]$Values) {
    if ($Values.Count -eq 0 -or @($Values | Where-Object { -not [double]::IsFinite($_) -or $_ -lt 0 }).Count -gt 0) {
        throw 'Median requires finite nonnegative samples.'
    }
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

function Get-Comparisons($Trials, [int]$Count) {
    $baseline = @($Trials | Where-Object { $_.role -eq 'baseline' -and -not $_.warmup -and $_.verified -and -not $_.error -and -not $_.cleanupError } | Sort-Object trial)
    $candidate = @($Trials | Where-Object { $_.role -eq 'candidate' -and -not $_.warmup -and $_.verified -and -not $_.error -and -not $_.cleanupError } | Sort-Object trial)
    # Partial or failed series must never masquerade as a three-trial comparison.
    if ($baseline.Count -ne $Count -or $candidate.Count -ne $Count) { return @() }
    foreach ($series in @(@{ items = $baseline }, @{ items = $candidate })) {
        if ((@($series.items.trial | Sort-Object -Unique) -join ',') -ne ((1..$Count) -join ',')) {
            throw 'Duplicate or missing trial number.'
        }
    }
    $names = @('systemCopy', 'systemDelete', 'freshDeploy.preflight', 'freshDeploy.execute',
        'overwrite.preflight', 'overwrite.execute', 'noop.preflight', 'noop.execute', 'clear.preflight', 'clear.execute')
    $comparisons = @()
    foreach ($name in ($names + @('freshDeploy.total', 'overwrite.total', 'noop.total', 'clear.total'))) {
        $summaries = @{}
        foreach ($role in @('baseline', 'candidate')) {
            $series = if ($role -eq 'baseline') { $baseline } else { $candidate }
            $samples = @()
            foreach ($trial in $series) {
                if ($name.EndsWith('.total')) {
                    $scenario = $name.Substring(0, $name.Length - '.total'.Length)
                    $samples += [double]$trial.stages["$scenario.preflight"].seconds + [double]$trial.stages["$scenario.execute"].seconds
                } else {
                    if (-not $trial.stages.Contains($name)) { throw "Missing measured stage: $role/$name" }
                    $samples += [double]$trial.stages[$name].seconds
                }
            }
            $summary = [ordered]@{ seconds = Get-Median $samples; sampleSeconds = $samples }
            if (-not $name.EndsWith('.total')) {
                foreach ($metric in @('cpuSeconds', 'managedAllocatedBytes', 'processHandlesBefore', 'processHandlesAfter')) {
                    $summary[$metric] = Get-Median @($series | ForEach-Object { [double]$_.stages[$name][$metric] })
                }
                $io = [ordered]@{}
                foreach ($metric in @('readOperations', 'writeOperations', 'otherOperations', 'readBytes', 'writeBytes', 'otherBytes')) {
                    $io[$metric] = Get-Median @($series | ForEach-Object { [double]$_.stages[$name].processIo[$metric] })
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

function Invoke-IntegrityTests([string]$Root, [string]$Role, [string]$Output) {
    $results = Join-Path $Output "regression/$Role"
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $project = Join-Path $Root 'tests/Rhi.Core.Tests/Rhi.Core.Tests.csproj'
    & dotnet test $project -c Release --filter 'FullyQualifiedName~ExecutionIntegrityTests' `
        --logger 'trx;LogFileName=integrity.trx' --results-directory $results 2>&1 | Tee-Object -FilePath (Join-Path $results 'test.log') | Out-Host
    $exitCode = $LASTEXITCODE
    $trxPath = Join-Path $results 'integrity.trx'
    if (-not (Test-Path -LiteralPath $trxPath)) { throw "$Role regression tests produced no TRX (build/discovery failure, exit $exitCode)." }
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
    $result = [ordered]@{ role = $Role; exitCode = $exitCode; total = [int]$counters.total
        executed = [int]$counters.executed; passed = [int]$counters.passed; failed = [int]$counters.failed; tests = $tests }
    # Persist the real failures, not a successful wrapper that hides test output.
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $results 'result.json') -Encoding utf8
    if ($result.total -ne 7 -or $result.executed -ne 7 -or $tests.Count -ne 7) { throw "$Role must execute exactly seven integrity cases." }
    if ($Role -eq 'baseline') {
        if ($exitCode -ne 1 -or $result.failed -ne 7) { throw 'Pinned baseline did not fail all seven expected integrity regressions.' }
    } elseif ($exitCode -ne 0 -or $result.failed -ne 0 -or $result.passed -ne 7) {
        throw 'Candidate did not pass all seven integrity regressions.'
    }
    return $result
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
    # Register immediately so startup/protocol failures also clean up this child.
    $script:workers.Add($worker)
    $ready = Read-WorkerResponse $worker
    if (-not $ready.ready -or $ready.protocolVersion -ne 1) { throw "Invalid $Role worker handshake." }
    return $worker
}

if ($FunctionsOnly) { return }
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $IsWindows) { throw 'Performance runs only on disposable Windows GitHub runners.' }
if (-not $BaselineRoot -or -not $CandidateRevision) { throw 'BaselineRoot and CandidateRevision are required.' }
$root = Split-Path $PSScriptRoot -Parent
$BaselineRoot = (Resolve-Path -LiteralPath $BaselineRoot).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$script:workers = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    schemaVersion = 2; status = 'inProgress'; baselineRevision = $BaselineRevision; candidateRevision = $CandidateRevision
    runner = [ordered]@{ name = $env:RUNNER_NAME; image = $env:ImageOS; imageVersion = $env:ImageVersion; processorCount = [Environment]::ProcessorCount }
    fixture = [ordered]@{ files = 990; bytesPerFile = 131072; payloadBytes = 129761280; executableBytes = 67108864; measuredTrialsPerRevision = $TrialCount; warmupFiles = 32 }
    methodology = @(
        'Identical current harness compiled separately against pinned baseline and candidate Core; no baseline production code is overlaid.',
        'One persistent worker per revision, one 32-file warmup per worker, then three fresh 990-file trials each. Workers never measure concurrently.',
        'Paired order is baseline/candidate, candidate/baseline, baseline/candidate. All original measurements and warmups are retained.',
        'Files are deterministically generated afresh per trial. Each scenario uses an independent target and workspace. No-op and clear setup deployments are excluded.',
        'All content, receipt, original-backup, executable, sentinel and source verification occurs outside timed phases. Preflight is separate from execution.',
        'System File.Copy/File.Delete are lower-level references, not equivalent guarantees: no receipts, durable journal, backup protection or explicit flush contract.',
        'Warm/JIT-primed synthetic runner only: fixture generation, setup and checks warm OS caches. No privileged cache flush, cold-cache claim or user-PC extrapolation.',
        'GetProcessIoCounters deltas are process-wide logical OS I/O, not physical disk traffic, hash bytes, copy bytes, open counts or JSON write counts; background runtime I/O may contribute.',
        'JSON inventories are before/after persisted file lengths, not write-call counts. Handle counts are endpoint samples, not a peak; transient retained handles can be higher.',
        'CPU seconds and managed allocated bytes are process-wide stage deltas. Medians use all three successful trials; slower results remain visible, with no significance claim.',
        'Scenario total is each trial preflight plus execution, excluding setup, verification and reporting, then the median of those totals.'
    )
    unavailableExactCounters = @('bytesHashed', 'bytesCopied', 'jsonWriteCalls', 'fileOpenCalls', 'directoryOpenCalls', 'pathConflictComparisons', 'peakOpenHandles')
    regressionGate = @(); executionOrder = @(); trials = @(); comparisons = @(); error = $null
}
function Save-Report {
    $report.generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $report.trials = @(Get-ChildItem -LiteralPath (Join-Path $output 'trials') -Filter '*.json' -ErrorAction SilentlyContinue |
        Sort-Object Name | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -AsHashtable })
    $report.regressionGate = @(Get-ChildItem -LiteralPath (Join-Path $output 'regression') -Filter 'result.json' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -AsHashtable })
    $report.comparisons = @(Get-Comparisons $report.trials $TrialCount)
    # Keep current.json self-contained because the existing draft uploader uploads it.
    $report | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $output 'current.json') -Encoding utf8
}
Save-Report
try {
    foreach ($entry in @(@{ root = $BaselineRoot; revision = $BaselineRevision }, @{ root = $root; revision = $CandidateRevision })) {
        $actual = & git -C $entry.root rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $entry.revision) { throw 'Checkout revision differs from the requested comparison.' }
    }
    $regression = 'tests/Rhi.Core.Tests/Deployment/ExecutionIntegrityTests.cs'
    Copy-Item -LiteralPath (Join-Path $root $regression) -Destination (Join-Path $BaselineRoot $regression)
    Invoke-IntegrityTests $BaselineRoot 'baseline' $output | Out-Null
    Save-Report
    Invoke-IntegrityTests $root 'candidate' $output | Out-Null
    Save-Report
    $roles = @{}
    foreach ($role in @('baseline', 'candidate')) {
        # Separate copies avoid obj/assets contamination while compiling exactly
        # the same harness sources and SDK settings for both Core implementations.
        $harness = Join-Path $output "harness/$role"
        $build = Join-Path $output "build/$role"
        New-Item -ItemType Directory -Path $harness -Force | Out-Null
        Copy-Item -Path (Join-Path $root 'tests/Rhi.Performance/*.cs'), (Join-Path $root 'tests/Rhi.Performance/*.csproj') -Destination $harness
        Copy-Item -LiteralPath (Join-Path $root 'Directory.Build.props') -Destination $harness
        $coreRoot = if ($role -eq 'baseline') { $BaselineRoot } else { $root }
        $revision = if ($role -eq 'baseline') { $BaselineRevision } else { $CandidateRevision }
        $core = Join-Path $coreRoot 'src/Rhi.Core/Rhi.Core.csproj'
        # Rewrite only the disposable harness copy, not the checked-in project.
        $projectPath = Join-Path $harness 'Rhi.Performance.csproj'
        [xml]$project = Get-Content -LiteralPath $projectPath -Raw
        $project.Project.ItemGroup.ProjectReference.Include = $core
        $project.Save($projectPath)
        & dotnet build $projectPath -c Release --output $build `
            2>&1 | Tee-Object -FilePath (Join-Path $output "$role-build.log") | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "$role performance harness build failed." }
        $roles[$role] = Start-Worker $role (Join-Path $build 'Rhi.Performance.dll') $revision $output
    }
    for ($trial = 0; $trial -le $TrialCount; $trial++) {
        $order = if ($trial -gt 0 -and $trial % 2 -eq 0) { @('candidate', 'baseline') } else { @('baseline', 'candidate') }
        foreach ($role in $order) {
            $report.executionOrder += [ordered]@{ role = $role; trial = $trial; warmup = $trial -eq 0 }
            Save-Report
            Write-Host "$role $(if ($trial -eq 0) { 'warmup' } else { "trial $trial/$TrialCount" })"
            $roles[$role].process.StandardInput.WriteLine((@{ trial = $trial; warmup = $trial -eq 0 } | ConvertTo-Json -Compress))
            $roles[$role].process.StandardInput.Flush()
            $response = Read-WorkerResponse $roles[$role]
            Save-Report
            if (-not $response.succeeded) { throw "$role trial $trial failed integrity or cleanup checks; retained its partial report." }
        }
    }
    if (@($report.comparisons).Count -ne 14) { throw 'Incomplete paired results.' }
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
    Save-Report
    if ($env:GITHUB_STEP_SUMMARY) {
        $summary = @('## Paired deployment performance', '', "Status: $($report.status)",
            "Baseline: $BaselineRevision", "Candidate: $CandidateRevision", '',
            'Three measured trials per revision after a 32-file warmup; same runner, 990 x 128 KiB per trial.',
            'Warm-cache synthetic results, not cold-disk or user-PC predictions. Positive change means slower.', '',
            '| Stage | Baseline median (s) | Candidate median (s) | Change |', '|---|---:|---:|---:|')
        foreach ($comparison in $report.comparisons) {
            Write-Host ('PERFORMANCE_SUMMARY:' + ($comparison | ConvertTo-Json -Depth 12 -Compress))
            $delta = if ($null -eq $comparison.candidateChangePercent) { 'n/a' } else { '{0:+0.00;-0.00;0.00}%' -f $comparison.candidateChangePercent }
            $summary += '| {0} | {1:F4} | {2:F4} | {3} |' -f $comparison.stage, $comparison.baseline.seconds, $comparison.candidate.seconds, $delta
        }
        if ($report.error) { $summary += @('', "Failure: $($report.error)") }
        $summary += @('', 'current.json retains all trials, OS I/O deltas, endpoint handles, JSON file inventories, integrity-gate results and measurement caveats.')
        $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
    }
}
