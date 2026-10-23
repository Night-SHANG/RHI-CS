using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || !OperatingSystem.IsWindows())
    throw new InvalidOperationException("Disposable Windows GitHub runner only.");
if (args.Length != 4 || args[0] != "--worker")
    throw new ArgumentException("Expected --worker, report directory, revision, and role.");

// The coordinator keeps both processes alive: warmup and measured trials share
// a runtime, but every trial owns newly generated source, targets, and workspace.
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
var protocolJson = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
Console.WriteLine(JsonSerializer.Serialize(new { ready = true, protocolVersion = 1 }, protocolJson));
while (Console.ReadLine() is { } line)
{
    if (line == "quit") break;
    var command = JsonSerializer.Deserialize<TrialCommand>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Missing trial command.");
    if (command.Trial < 0 || command.Warmup != (command.Trial == 0))
        throw new InvalidDataException("Trial zero is warmup; measured trials must be positive.");
    var report = new TrialReport(args[2], args[3], command.Trial, command.Warmup,
        command.Warmup ? 32 : Fixture.FileCount);
    var path = Path.Combine(output, $"{args[3]}-{(command.Warmup ? "warmup" : $"trial-{command.Trial}")}.json");
    // Checkpoint outside all timed phases, including when a later phase fails.
    void Save() => File.WriteAllText(path, JsonSerializer.Serialize(report, json));
    var root = Path.Combine(Path.GetTempPath(), "RhiBenchmark", Guid.NewGuid().ToString("N"));
    try
    {
        var fixture = new Fixture(root, report.Files);
        RunReference(fixture, report, Save);
        foreach (var scenario in new[] { "freshDeploy", "overwrite", "noop", "clear" })
            RunScenario(fixture, scenario, report, Save);
        fixture.VerifySources();
        report.Verified = true;
    }
    catch (Exception error) { report.Error = error.ToString(); }
    finally
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (Exception error) { report.CleanupError = error.ToString(); }
        Save();
    }
    Console.WriteLine(JsonSerializer.Serialize(new { reportPath = path, verified = report.Verified,
        succeeded = report.Verified && report.Error is null && report.CleanupError is null }, protocolJson));
}

static void RunReference(Fixture fixture, TrialReport report, Action save)
{
    var direct = Directory.CreateDirectory(Path.Combine(fixture.Root, "system-copy")).FullName;
    Measure(report, "systemCopy", null, () =>
    {
        foreach (var name in fixture.Hashes.Keys) File.Copy(Path.Combine(fixture.Source, name), Path.Combine(direct, name));
        return true;
    }, save);
    Fixture.VerifyFiles(direct, fixture.Hashes);
    Measure(report, "systemDelete", null, () =>
    {
        foreach (var name in fixture.Hashes.Keys) File.Delete(Path.Combine(direct, name));
        return true;
    }, save);
    Fixture.Require(!Directory.EnumerateFileSystemEntries(direct).Any(), "System delete left files behind.");
    fixture.VerifySources();
}

static void RunScenario(Fixture fixture, string scenario, TrialReport report, Action save)
{
    var target = Directory.CreateDirectory(Path.Combine(fixture.Root, scenario, "game")).FullName;
    var exe = Path.Combine(target, "fixture.exe");
    File.Copy(fixture.Executable, exe);
    var sentinel = Path.Combine(target, "unmanaged.keep");
    File.WriteAllText(sentinel, "Do not modify unrelated game files.");
    var sentinelHash = Fixture.Hash(sentinel);
    var originals = scenario == "overwrite" ? Fixture.WriteResources(target, report.Files, 555120)
        : new Dictionary<string, string>();
    using var store = WorkspaceStore.Open(Path.Combine(fixture.Root, scenario, "Data"), create: true);
    var game = new GameEntry(Guid.NewGuid(), "Benchmark", target, exe, target, "DirectX 12", "x64", "Manual");
    var version = new ResourceVersion(Guid.NewGuid(), "Synthetic files", fixture.Source, null, ResourceKind.Files);
    var library = new ResourceLibrary(Guid.NewGuid(), "Benchmark", "", [version]);
    store.Save(store.State with { Games = [game], Libraries = [library] });
    var planner = new DeploymentPlanner(store);
    var engine = new DeploymentEngine(store);
    BatchPlan Build(OperationKind kind) => planner.Build(store.State, game.Id,
        [new(library.Id, kind == OperationKind.Clear ? null : version.Id, kind)]);
    void Execute(BatchPlan plan)
    {
        Fixture.Require(plan.CanExecute, "Preflight blocked: " + JsonSerializer.Serialize(plan.Issues));
        var result = engine.Execute(plan, new(true, true));
        Fixture.Require(result.Count == 1 && result[0].Status == OperationStatus.Succeeded,
            "Operation failed: " + JsonSerializer.Serialize(result));
    }

    if (scenario is "noop" or "clear")
    {
        // Build a legitimate independent installed fixture, excluded from timings.
        Execute(Build(OperationKind.Deploy));
        VerifyInstalled(store, target, fixture.Hashes, originals);
    }
    var kind = scenario switch { "noop" => OperationKind.Change, "clear" => OperationKind.Clear, _ => OperationKind.Deploy };
    var plan = Measure(report, scenario + ".preflight", store.Root, () => Build(kind), save);
    Fixture.Require(plan.CanExecute, "Preflight blocked: " + JsonSerializer.Serialize(plan.Issues));
    if (scenario == "noop")
        Fixture.Require(plan.Libraries.Single().Changes.All(c => c.BeforeHash == c.DesiredHash), "No-op fixture is not unchanged.");
    var result = Measure(report, scenario + ".execute", store.Root, () => engine.Execute(plan, new(true, true)), save);
    Fixture.Require(result.Count == 1 && result[0].Status == OperationStatus.Succeeded,
        "Operation failed: " + JsonSerializer.Serialize(result));
    if (scenario == "clear")
    {
        Fixture.Require(store.State.Receipts.Length == 0, "Clear retained a receipt.");
        Fixture.Require(Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Count() == 2, "Clear retained managed/temporary files.");
        Fixture.Require(!Directory.EnumerateFileSystemEntries(Path.Combine(store.Root, "blobs")).Any(), "Clear unnecessarily cached source files.");
    }
    else VerifyInstalled(store, target, fixture.Hashes, originals);
    Fixture.Require(Fixture.Hash(exe) == fixture.ExecutableHash && Fixture.Hash(sentinel) == sentinelHash,
        "Unrelated executable or sentinel changed.");
    Fixture.Require(store.Pending().Length == 0, "Operation left a pending journal.");
    fixture.VerifySources();
    report.VerifiedScenarios.Add(scenario);
    save();
}

static void VerifyInstalled(WorkspaceStore store, string target, Dictionary<string, string> expected,
    Dictionary<string, string> originals)
{
    Fixture.VerifyFiles(target, expected);
    Fixture.Require(Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Count() == expected.Count + 2,
        "Unexpected temporary or managed files in target.");
    var receipt = store.State.Receipts.Single();
    Fixture.Require(receipt.Files.Length == expected.Count, "Receipt file count differs.");
    foreach (var file in receipt.Files)
    {
        Fixture.Require(expected.TryGetValue(file.RelativePath, out var hash) && file.InstalledHash == hash,
            "Installed receipt hash differs: " + file.RelativePath);
        Fixture.Require(file.OriginalHash == originals.GetValueOrDefault(file.RelativePath), "Original receipt hash differs.");
    }
    var blobs = Directory.EnumerateFiles(Path.Combine(store.Root, "blobs")).ToArray();
    Fixture.Require(blobs.Length == originals.Values.Distinct().Count(), "Unexpected source cache or missing original backup.");
    foreach (var hash in originals.Values)
        Fixture.Require(Fixture.Hash(store.Blobs.PathFor(hash)) == hash, "Original backup content differs.");
    Fixture.Require(!Directory.EnumerateFileSystemEntries(Path.Combine(store.Root, "protection")).Any(),
        "Unmodified fixture unexpectedly needed protection copies.");
}

static T Measure<T>(TrialReport report, string name, string? workspace, Func<T> action, Action save)
{
    var beforeJson = JsonFiles.Read(workspace);
    using var process = Process.GetCurrentProcess();
    var beforeCpu = process.TotalProcessorTime;
    var beforeHandles = process.HandleCount;
    var beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
    var beforeIo = ProcessIo.Read();
    var watch = Stopwatch.StartNew();
    try { return action(); }
    finally
    {
        watch.Stop();
        var afterIo = ProcessIo.Read();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - beforeAllocated;
        process.Refresh();
        var cpu = (process.TotalProcessorTime - beforeCpu).TotalSeconds;
        report.Stages.Add(name, new(watch.Elapsed.TotalSeconds, cpu, allocated,
            beforeHandles, process.HandleCount, afterIo.Minus(beforeIo), beforeJson, JsonFiles.Read(workspace)));
        save();
    }
}

sealed record TrialCommand(int Trial, bool Warmup);
sealed class TrialReport(string revision, string role, int trial, bool warmup, int files)
{
    public string Revision { get; } = revision;
    public string Role { get; } = role;
    public int Trial { get; } = trial;
    public bool Warmup { get; } = warmup;
    public int Files { get; } = files;
    public long Bytes { get; } = (long)files * Fixture.FileBytes;
    public long ExecutableBytes { get; } = Fixture.ExecutableBytes;
    public string Runtime { get; } = RuntimeInformation.FrameworkDescription;
    public string OperatingSystem { get; } = RuntimeInformation.OSDescription;
    public Dictionary<string, StageMeasurement> Stages { get; } = [];
    public List<string> VerifiedScenarios { get; } = [];
    public bool Verified { get; set; }
    public string? Error { get; set; }
    public string? CleanupError { get; set; }
}
sealed record StageMeasurement(double Seconds, double CpuSeconds, long ManagedAllocatedBytes,
    int ProcessHandlesBefore, int ProcessHandlesAfter, ProcessIo ProcessIo, JsonFile[] JsonBefore, JsonFile[] JsonAfter);
sealed record JsonFile(string Path, long Bytes);
static class JsonFiles
{
    public static JsonFile[] Read(string? root) => root is null ? [] : Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => new JsonFile(Path.GetRelativePath(root, path), new FileInfo(path).Length)).ToArray();
}

sealed class Fixture
{
    public const int FileCount = 990, FileBytes = 128 * 1024, ExecutableBytes = 64 * 1024 * 1024;
    public string Root { get; }
    public string Source { get; }
    public string Executable { get; }
    public string ExecutableHash { get; }
    public Dictionary<string, string> Hashes { get; }
    public Fixture(string root, int count)
    {
        Root = Directory.CreateDirectory(root).FullName;
        Source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        Hashes = WriteResources(Source, count, 120990);
        Executable = Path.Combine(root, "executable.bin");
        var data = new byte[FileBytes];
        var random = new Random(120991);
        using (var output = File.Create(Executable))
            for (var i = 0; i < ExecutableBytes / FileBytes; i++) { random.NextBytes(data); output.Write(data); }
        ExecutableHash = Hash(Executable);
    }
    public static Dictionary<string, string> WriteResources(string target, int count, int seed)
    {
        var hashes = new Dictionary<string, string>();
        var random = new Random(seed);
        var data = new byte[FileBytes];
        for (var i = 0; i < count; i++)
        {
            random.NextBytes(data);
            var name = $"shader-{i:D4}.fx";
            File.WriteAllBytes(Path.Combine(target, name), data);
            hashes.Add(name, Convert.ToHexString(SHA256.HashData(data)));
        }
        return hashes;
    }
    // Independent of production BlobStore.Hash so the integrity oracle is not
    // accidentally changed by the implementation under comparison.
    public static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
    public static void VerifyFiles(string directory, Dictionary<string, string> hashes)
    {
        foreach (var (name, expected) in hashes)
            Require(Hash(Path.Combine(directory, name)) == expected, "Content mismatch: " + name);
    }
    public void VerifySources()
    {
        VerifyFiles(Source, Hashes);
        Require(Directory.EnumerateFiles(Source).Count() == Hashes.Count, "Source file set changed.");
        Require(Hash(Executable) == ExecutableHash, "Executable template changed.");
    }
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}

sealed record ProcessIo(ulong ReadOperations, ulong WriteOperations, ulong OtherOperations,
    ulong ReadBytes, ulong WriteBytes, ulong OtherBytes)
{
    public static ProcessIo Read()
    {
        if (!GetProcessIoCounters(GetCurrentProcess(), out var counters))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new(counters.ReadOperations, counters.WriteOperations, counters.OtherOperations,
            counters.ReadBytes, counters.WriteBytes, counters.OtherBytes);
    }
    public ProcessIo Minus(ProcessIo before) => new(ReadOperations - before.ReadOperations,
        WriteOperations - before.WriteOperations, OtherOperations - before.OtherOperations,
        ReadBytes - before.ReadBytes, WriteBytes - before.WriteBytes, OtherBytes - before.OtherBytes);
    [StructLayout(LayoutKind.Sequential)]
    private struct Counters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out Counters counters);
}
