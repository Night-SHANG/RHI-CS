using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Rhi.Core.Storage;

namespace Rhi.CopyStrategies;

internal static class Program
{
    private const int Files = 330;
    private const int WarmupFiles = 32;
    private const int FileBytes = 128 * 1024;
    private const int Rounds = 3;
    private static readonly Stopwatch Budget = Stopwatch.StartNew();
    private static readonly TimeSpan BudgetLimit = TimeSpan.FromMinutes(6);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Strategy[] Strategies =
    [
        new("buffered-current", false, true, 1, 1, false, false,
            "Current per-file Flush(true) boundary; isolated reference, not a full deployment."),
        new("write-through-only", true, false, 1, 0, false, false,
            "WEAKER DIAGNOSTIC ONLY: WRITE_THROUGH is not an all-hardware substitute for FlushFileBuffers; not a publication candidate."),
        new("write-through-plus-flush", true, true, 1, 1, false, false,
            "Write-through control retaining the same post-rename Flush(true)."),
        new("buffered-batch32-flush1", false, true, 32, 1, true, false,
            "Every handle receives Flush(true), then all are closed before batch success. Deferred per-file barriers require a separate engine/recovery audit."),
        new("buffered-batch32-flush4", false, true, 32, 4, true, false,
            "Same Flush(true) call count and batch-success barrier as flush1; only maximum flush parallelism changes. Not production adoption."),
        new("native-copy-weaker-reference", true, false, 1, 0, false, true,
            "WEAKER REFERENCE ONLY: File.Copy, then existing read/delete WRITE_THROUGH handle and rename; no timed source hash or explicit disk flush.")
    ];
    private static readonly Comparison[] Comparisons =
    [
        new(Strategies[0], Strategies[1]),
        new(Strategies[0], Strategies[2]),
        new(Strategies[0], Strategies[3]),
        new(Strategies[3], Strategies[4]),
        new(Strategies[0], Strategies[5])
    ];

    public static int Main(string[] args)
    {
        string? root = null;
        string? error = null;
        string? cleanupError = null;
        object? comparisons = null;
        var trials = new List<Trial>();
        try
        {
            if (args.Length != 0) throw new ArgumentException("No options: the workload and safety limits are fixed.");
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
                || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
                throw new InvalidOperationException("This diagnostic runs only on a disposable GitHub-hosted Windows runner.");

            root = Path.Combine(Path.GetTempPath(), "RhiCopyStrategies-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Temporary fixture collision.");
            Directory.CreateDirectory(root);
            Emit("plan", new
            {
                schemaVersion = 1, filesPerMeasuredTrial = Files, bytesPerFile = FileBytes,
                warmupFilesPerStrategy = WarmupFiles, pairedRoundsPerComparison = Rounds,
                measuredTrials = Comparisons.Length * 2 * Rounds,
                generatedSourceBytesPerRound = Files * (long)FileBytes,
                measuredCopyBytesTotal = Files * (long)FileBytes * Comparisons.Length * 2 * Rounds,
                softBudgetMinutes = BudgetLimit.TotalMinutes, workflowHardTimeoutMinutes = 10,
                strategies = Strategies, comparisons = Comparisons,
                timingScope = "Copy/hash, managed pre-flush, exact-handle rename, post-rename disk flush, and close. No Core, UI, receipts, journals, protection, crash or power-loss tests.",
                pairing = "Independent destination per trial; identical source bytes for both members of each pair. Each comparison's baseline first in rounds 1/3, challenger first in round 2; comparison order rotates. Batch32/max4 is paired directly with batch32/max1, which is separately paired with current.",
                cacheState = "Warm-cache descriptive microbenchmark. Setup, warmup, and independent verification read sources/targets; no cache flushing.",
                interpretation = "No speed threshold or automatic production recommendation. Three pairs are descriptive, not statistical proof; no extrapolation to 990 files or the user's computer."
            });
            Emit("environment", DescribeEnvironment(root));

            var warmup = Fixture.Create(Path.Combine(root, "warmup-source"), WarmupFiles, 412031);
            foreach (var strategy in Strategies)
                RunAndRecord(warmup, root, strategy, 0, "warmup", true, trials);
            Directory.Delete(warmup.Directory, recursive: true);

            for (var round = 1; round <= Rounds; round++)
            {
                CheckBudget();
                var fixture = Fixture.Create(Path.Combine(root, $"source-{round}"), Files, 412031 + round);
                for (var offset = 0; offset < Comparisons.Length; offset++)
                {
                    var comparison = Comparisons[(offset + round - 1) % Comparisons.Length];
                    var pair = $"round-{round}-{comparison.Challenger.Id}";
                    var order = round % 2 == 1
                        ? new[] { comparison.Baseline, comparison.Challenger }
                        : new[] { comparison.Challenger, comparison.Baseline };
                    foreach (var strategy in order)
                        RunAndRecord(fixture, root, strategy, round, pair, false, trials);
                }
                fixture.VerifySources();
                Directory.Delete(fixture.Directory, recursive: true);
            }
            comparisons = Summarize(trials);
        }
        catch (Exception failure) { error = failure.ToString(); }
        finally
        {
            if (root is not null)
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
                catch (Exception failure) { cleanupError = failure.ToString(); }
            }
        }

        var succeeded = error is null && cleanupError is null && comparisons is not null;
        Emit("completion", new
        {
            succeeded, error, cleanupError, elapsedSeconds = Budget.Elapsed.TotalSeconds,
            completedWarmups = trials.Count(t => t.Warmup && t.Succeeded),
            completedMeasuredTrials = trials.Count(t => !t.Warmup && t.Succeeded),
            comparisons = succeeded ? comparisons : null,
            productionChangeAuthorizedByBenchmark = false,
            powerLossDurabilityProven = false
        });
        return succeeded ? 0 : 1;
    }

    private static void RunAndRecord(Fixture fixture, string root, Strategy strategy, int round,
        string pair, bool warmup, List<Trial> trials)
    {
        CheckBudget();
        var trial = new Trial(strategy, round, pair, warmup, fixture.Entries.Length);
        trials.Add(trial);
        var target = Path.Combine(root, "target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        Emit("trial-start", new { strategy = strategy.Id, round, pair, warmup });
        try
        {
            var start = Stopwatch.GetTimestamp();
            try { Execute(fixture, target, strategy, trial); }
            finally { trial.Timing.TotalMilliseconds = MillisecondsSince(start); }

            // Independent SHA-256 + exact file count checks, never in timed stages.
            fixture.VerifyTargets(target);
            fixture.VerifySources();
            trial.ContentVerified = true;
            ValidateCounts(trial);
        }
        catch (Exception failure) { trial.Error = failure.ToString(); }
        finally
        {
            try { Directory.Delete(target, recursive: true); trial.CleanupVerified = !Directory.Exists(target); }
            catch (Exception failure) { trial.CleanupError = failure.ToString(); }
            Emit("trial", trial);
        }
        if (!trial.Succeeded) throw new InvalidOperationException($"Trial {pair}/{strategy.Id} failed; see its JSON error. No successful aggregate will be produced.");
    }

    private static void Execute(Fixture fixture, string target, Strategy strategy, Trial trial)
    {
        for (var first = 0; first < fixture.Entries.Length; first += strategy.BatchSize)
        {
            CheckBudget();
            var held = new List<FileStream>(strategy.BatchSize);
            try
            {
                for (var index = first; index < Math.Min(first + strategy.BatchSize, fixture.Entries.Length); index++)
                {
                    CheckBudget();
                    var entry = fixture.Entries[index];
                    var source = Path.Combine(fixture.Directory, entry.Name);
                    var stage = Path.Combine(target, entry.Name + ".next");
                    var final = Path.Combine(target, entry.Name);
                    trial.FilesAttempted++;

                    if (strategy.NativeCopy)
                    {
                        var start = Stopwatch.GetTimestamp();
                        try { File.Copy(source, stage, overwrite: false); }
                        finally { trial.Timing.NativeCopyMilliseconds += MillisecondsSince(start); }
                    }

                    var openStart = Stopwatch.GetTimestamp();
                    FileStream output;
                    try { output = NativeFiles.Open(stage, create: !strategy.NativeCopy, writeThrough: strategy.WriteThrough); }
                    finally { trial.Timing.OpenMilliseconds += MillisecondsSince(openStart); }
                    held.Add(output);
                    trial.HeldStagingHandles++;
                    trial.PeakHeldStagingHandles = Math.Max(trial.PeakHeldStagingHandles, trial.HeldStagingHandles);

                    if (!strategy.NativeCopy)
                    {
                        var copyStart = Stopwatch.GetTimestamp();
                        try
                        {
                            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                            using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                                FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
                            CopyBuffer.CopyAndHash(input, output, hash, static () => { }, ArrayPool<byte>.Shared);
                            var copied = input.Position;
                            Require(copied == FileBytes && output.Position == FileBytes, "Copy byte count differs.");
                            trial.ManagedSourceReadBytes += copied;
                            trial.ManagedWriteBytes += output.Position;
                            trial.TimedHashBytes += copied;
                            Require(CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), entry.Hash), "Timed source hash differs.");
                            trial.TimedSourceHashes++;
                        }
                        finally { trial.Timing.CopyAndHashMilliseconds += MillisecondsSince(copyStart); }

                        var preFlushStart = Stopwatch.GetTimestamp();
                        try { output.Flush(flushToDisk: false); trial.ManagedPreFlushCalls++; }
                        finally { trial.Timing.ManagedPreFlushMilliseconds += MillisecondsSince(preFlushStart); }
                    }
                    else
                    {
                        Require(output.Length == FileBytes, "Native copy byte count differs.");
                        trial.NativeCopiedLengthBytes += output.Length;
                    }

                    var renameStart = Stopwatch.GetTimestamp();
                    try { NativeFiles.Rename(output, final); trial.RenameCalls++; }
                    finally { trial.Timing.RenameMilliseconds += MillisecondsSince(renameStart); }
                }

                if (strategy.ExplicitDiskFlush)
                {
                    var flushStart = Stopwatch.GetTimestamp();
                    try
                    {
                        if (strategy.Batched)
                            Parallel.ForEach(held, new ParallelOptions { MaxDegreeOfParallelism = strategy.MaximumFlushWorkers }, trial.DiskFlush);
                        else
                            foreach (var file in held) trial.DiskFlush(file);
                    }
                    finally { trial.Timing.DiskFlushWallMilliseconds += MillisecondsSince(flushStart); }
                }

                // No success boundary until EVERY flush has returned and every
                // exact staging handle has closed. Parallel.ForEach joins workers.
                var completed = held.Count;
                CloseAll(held, trial);
                trial.CompletedFiles += completed;
                trial.CompletedBatches++;
            }
            finally
            {
                // On any failure, keep failure visible and close remaining handles.
                // There is no commit/receipt operation in this diagnostic.
                CloseAll(held, trial);
            }
        }
    }

    private static void CloseAll(List<FileStream> held, Trial trial)
    {
        if (held.Count == 0) return;
        var start = Stopwatch.GetTimestamp();
        List<Exception>? errors = null;
        try
        {
            foreach (var file in held)
            {
                try { file.Dispose(); trial.ClosedStagingHandles++; }
                catch (Exception failure) { (errors ??= []).Add(failure); }
                finally { trial.HeldStagingHandles--; }
            }
        }
        finally
        {
            held.Clear();
            trial.Timing.CloseMilliseconds += MillisecondsSince(start);
        }
        if (errors is not null) throw new AggregateException("Closing staging handles failed.", errors);
    }

    private static void ValidateCounts(Trial trial)
    {
        var count = trial.Files;
        Require(trial.CompletedFiles == count && trial.RenameCalls == count && trial.ClosedStagingHandles == count,
            "Incomplete copy/rename/close counts.");
        Require(trial.HeldStagingHandles == 0 && trial.ActiveDiskFlushCalls == 0, "Owned handles or disk flushes remain active.");
        Require(trial.PeakHeldStagingHandles <= trial.Strategy.BatchSize, "Staging handle bound exceeded.");
        Require(trial.PeakConcurrentDiskFlushCalls <= trial.Strategy.MaximumFlushWorkers, "Flush concurrency bound exceeded.");
        var expectedFlushes = trial.Strategy.ExplicitDiskFlush ? count : 0;
        Require(trial.DiskFlushAttempts == expectedFlushes && trial.SuccessfulDiskFlushCalls == expectedFlushes,
            "Missing or failed explicit disk flush.");
        if (!trial.Strategy.NativeCopy)
            Require(trial.ManagedSourceReadBytes == count * (long)FileBytes && trial.ManagedWriteBytes == count * (long)FileBytes
                && trial.TimedHashBytes == count * (long)FileBytes && trial.TimedSourceHashes == count && trial.ManagedPreFlushCalls == count,
                "Copy/hash/pre-flush count mismatch.");
        else
            Require(trial.NativeCopiedLengthBytes == count * (long)FileBytes && trial.TimedSourceHashes == 0
                && trial.ManagedPreFlushCalls == 0, "Weaker reference counts mismatch.");
        trial.CountsVerified = true;
    }

    private static object Summarize(List<Trial> trials)
    {
        Require(trials.Count == Strategies.Length + Comparisons.Length * 2 * Rounds && trials.All(t => t.Succeeded),
            "Missing or failed trials; aggregation refused.");
        Require(Strategies.All(s => trials.Count(t => t.Warmup && t.Strategy.Id == s.Id) == 1), "Missing warmup.");
        return Comparisons.Select(comparison =>
        {
            var challenger = comparison.Challenger;
            var pairs = Enumerable.Range(1, Rounds).Select(round =>
            {
                var key = $"round-{round}-{challenger.Id}";
                var members = trials.Where(t => !t.Warmup && t.Pair == key).ToArray();
                Require(members.Length == 2, "Incomplete pair.");
                var baseline = members.Single(t => t.Strategy.Id == comparison.Baseline.Id);
                var candidate = members.Single(t => t.Strategy.Id == challenger.Id);
                Require(baseline.Timing.TotalMilliseconds > 0 && candidate.Timing.TotalMilliseconds > 0, "Invalid elapsed time.");
                return new
                {
                    round, executionOrder = members.Select(t => t.Strategy.Id).ToArray(),
                    baselineMilliseconds = baseline.Timing.TotalMilliseconds,
                    challengerMilliseconds = candidate.Timing.TotalMilliseconds,
                    challengerOverBaseline = candidate.Timing.TotalMilliseconds / baseline.Timing.TotalMilliseconds,
                    baselinePhases = baseline.Timing, challengerPhases = candidate.Timing,
                    candidate.PeakConcurrentDiskFlushCalls, candidate.PeakHeldStagingHandles
                };
            }).ToArray();
            return new
            {
                baseline = comparison.Baseline.Id, challenger = challenger.Id, challenger.Interpretation,
                medianBaselineMilliseconds = Median(pairs.Select(p => p.baselineMilliseconds)),
                medianChallengerMilliseconds = Median(pairs.Select(p => p.challengerMilliseconds)),
                medianPairedRatio = Median(pairs.Select(p => p.challengerOverBaseline)),
                medianPairedChangePercent = (Median(pairs.Select(p => p.challengerOverBaseline)) - 1) * 100,
                pairs,
                productionRecommendation = "None. Timing alone cannot establish durability, rollback, crash recovery or bounded-handle integration safety."
            };
        }).ToArray();
    }

    private static object DescribeEnvironment(string root)
    {
        string fileSystem = "unknown";
        string driveType = "unknown";
        string? fileSystemQueryError = null;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            fileSystem = drive.DriveFormat;
            driveType = drive.DriveType.ToString();
        }
        catch (Exception failure) { fileSystemQueryError = failure.GetType().Name; }
        return new
        {
            os = RuntimeInformation.OSDescription, osVersion = Environment.OSVersion.VersionString,
            runtime = RuntimeInformation.FrameworkDescription, runtimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(), processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            logicalProcessorCount = Environment.ProcessorCount, fileSystem, driveType, fileSystemQueryError,
            runnerImage = Environment.GetEnvironmentVariable("ImageOS") ?? "unknown",
            runnerImageVersion = Environment.GetEnvironmentVariable("ImageVersion") ?? "unknown",
            revision = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "unknown",
            antivirus = "unknown; not queried or changed", physicalStorage = "unknown; not queried",
            securitySettings = "not queried or changed", managedBufferBytes = 128 * 1024,
            ioCounters = "Managed stream positions only, not physical I/O. Native File.Copy internal bytes are not observed."
        };
    }

    internal static void CheckBudget()
    {
        if (Budget.Elapsed > BudgetLimit) throw new TimeoutException("Six-minute soft budget exceeded. No incomplete aggregate is valid. In-flight synchronous OS calls cannot be interrupted; the workflow has a ten-minute hard limit.");
    }
    internal static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
    internal static double MillisecondsSince(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    private static double Median(IEnumerable<double> values)
    { var sorted = values.Order().ToArray(); Require(sorted.Length == Rounds, "Exactly three paired rounds required."); return sorted[sorted.Length / 2]; }
    private static void Emit(string type, object value) => Console.WriteLine(JsonSerializer.Serialize(new { type, value }, Json));

    private sealed record Strategy(string Id, bool WriteThrough, bool ExplicitDiskFlush, int BatchSize,
        int MaximumFlushWorkers, bool Batched, bool NativeCopy, string Interpretation);
    private sealed record Comparison(Strategy Baseline, Strategy Challenger);

    private sealed class Timing
    {
        public double TotalMilliseconds { get; set; }
        public double OpenMilliseconds { get; set; }
        public double CopyAndHashMilliseconds { get; set; }
        public double NativeCopyMilliseconds { get; set; }
        public double ManagedPreFlushMilliseconds { get; set; }
        public double RenameMilliseconds { get; set; }
        public double DiskFlushWallMilliseconds { get; set; }
        public double CloseMilliseconds { get; set; }
        public double UnattributedMilliseconds => TotalMilliseconds - OpenMilliseconds - CopyAndHashMilliseconds
            - NativeCopyMilliseconds - ManagedPreFlushMilliseconds - RenameMilliseconds - DiskFlushWallMilliseconds - CloseMilliseconds;
    }

    private sealed class Trial(Strategy strategy, int round, string pair, bool warmup, int files)
    {
        private long diskFlushTicks;
        private int activeDiskFlushCalls;
        private int peakConcurrentDiskFlushCalls;
        private int diskFlushAttempts;
        private int successfulDiskFlushCalls;
        public Strategy Strategy { get; } = strategy;
        public int Round { get; } = round;
        public string Pair { get; } = pair;
        public bool Warmup { get; } = warmup;
        public int Files { get; } = files;
        public Timing Timing { get; } = new();
        public int FilesAttempted { get; set; }
        public int CompletedFiles { get; set; }
        public int CompletedBatches { get; set; }
        public int HeldStagingHandles { get; set; }
        public int PeakHeldStagingHandles { get; set; }
        public int ClosedStagingHandles { get; set; }
        public int TimedSourceHashes { get; set; }
        public int ManagedPreFlushCalls { get; set; }
        public int RenameCalls { get; set; }
        public long ManagedSourceReadBytes { get; set; }
        public long ManagedWriteBytes { get; set; }
        public long TimedHashBytes { get; set; }
        public long NativeCopiedLengthBytes { get; set; }
        public int DiskFlushAttempts => Volatile.Read(ref diskFlushAttempts);
        public int SuccessfulDiskFlushCalls => Volatile.Read(ref successfulDiskFlushCalls);
        public int ActiveDiskFlushCalls => Volatile.Read(ref activeDiskFlushCalls);
        public int PeakConcurrentDiskFlushCalls => Volatile.Read(ref peakConcurrentDiskFlushCalls);
        public double DiskFlushCallSumMilliseconds => Interlocked.Read(ref diskFlushTicks) * 1000.0 / Stopwatch.Frequency;
        public bool ContentVerified { get; set; }
        public bool CountsVerified { get; set; }
        public bool CleanupVerified { get; set; }
        public string? Error { get; set; }
        public string? CleanupError { get; set; }
        public bool Succeeded => ContentVerified && CountsVerified && CleanupVerified && Error is null && CleanupError is null;

        public void DiskFlush(FileStream file)
        {
            Interlocked.Increment(ref diskFlushAttempts);
            var active = Interlocked.Increment(ref activeDiskFlushCalls);
            int old;
            do { old = Volatile.Read(ref peakConcurrentDiskFlushCalls); }
            while (active > old && Interlocked.CompareExchange(ref peakConcurrentDiskFlushCalls, active, old) != old);
            var start = Stopwatch.GetTimestamp();
            try { file.Flush(flushToDisk: true); Interlocked.Increment(ref successfulDiskFlushCalls); }
            finally
            {
                Interlocked.Add(ref diskFlushTicks, Stopwatch.GetTimestamp() - start);
                Interlocked.Decrement(ref activeDiskFlushCalls);
            }
        }
    }

    private sealed record Entry(string Name, byte[] Hash);
    private sealed class Fixture(string directory, Entry[] entries)
    {
        public string Directory { get; } = directory;
        public Entry[] Entries { get; } = entries;

        public static Fixture Create(string directory, int count, int seed)
        {
            System.IO.Directory.CreateDirectory(directory);
            var random = new Random(seed);
            var bytes = new byte[FileBytes];
            var entries = new Entry[count];
            for (var index = 0; index < count; index++)
            {
                CheckBudget();
                random.NextBytes(bytes);
                var name = $"resource-{index:D4}.bin";
                File.WriteAllBytes(Path.Combine(directory, name), bytes);
                entries[index] = new Entry(name, SHA256.HashData(bytes));
            }
            var fixture = new Fixture(directory, entries);
            fixture.VerifySources();
            return fixture;
        }

        public void VerifySources() => VerifyTargets(Directory);
        public void VerifyTargets(string directory)
        {
            Require(System.IO.Directory.EnumerateFileSystemEntries(directory).Count() == Entries.Length,
                "Missing/extra fixture entries or leftover staging names.");
            foreach (var entry in Entries)
            {
                CheckBudget();
                using var input = File.OpenRead(Path.Combine(directory, entry.Name));
                Require(input.Length == FileBytes && CryptographicOperations.FixedTimeEquals(SHA256.HashData(input), entry.Hash),
                    "Independent verification failed: " + entry.Name);
            }
        }
    }
}
