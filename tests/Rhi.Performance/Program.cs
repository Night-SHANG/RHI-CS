using System.Diagnostics;
using System.Text.Json;
using Rhi.Core.Deployment;
using Rhi.Core.Models;
using Rhi.Core.Storage;

if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
    throw new InvalidOperationException("Disposable GitHub runner only.");
if (args.Length != 2) throw new ArgumentException("Expected report path and revision label.");
var root = Path.Combine(Path.GetTempPath(), "RhiBenchmark", Guid.NewGuid().ToString("N"));
var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
var target = Directory.CreateDirectory(Path.Combine(root, "game")).FullName;
var random = new Random(120990);
var data = new byte[128 * 1024];
var hashes = new Dictionary<string, string>();
for (var i = 0; i < 990; i++)
{
    random.NextBytes(data);
    var name = $"shader-{i:D4}.fx";
    var path = Path.Combine(source, name);
    File.WriteAllBytes(path, data);
    hashes.Add(name, BlobStore.Hash(path)!);
}
var exe = Path.Combine(target, "fixture.exe");
using (var stream = File.Create(exe))
    for (var i = 0; i < 512; i++) { random.NextBytes(data); stream.Write(data); }
using var store = WorkspaceStore.Open(Path.Combine(root, "Data"), create: true);
var game = new GameEntry(Guid.NewGuid(), "Benchmark", target, exe, target, "DirectX 12", "x64", "Manual");
var version = new ResourceVersion(Guid.NewGuid(), "990 files", source, null, ResourceKind.Files);
var library = new ResourceLibrary(Guid.NewGuid(), "Benchmark", "", [version]);
store.Save(store.State with { Games = [game], Libraries = [library] });
var stages = new Dictionary<string, double>();
var watch = Stopwatch.StartNew();
var plan = new DeploymentPlanner(store).Build(store.State, game.Id, [new(library.Id, version.Id, OperationKind.Deploy)]);
stages["preflightSeconds"] = watch.Elapsed.TotalSeconds;
if (!plan.CanExecute) throw new InvalidOperationException(JsonSerializer.Serialize(plan.Issues));
watch.Restart();
var result = new DeploymentEngine(store).Execute(plan, new(true, true));
stages["deploySeconds"] = watch.Elapsed.TotalSeconds;
if (result.Single().Status != OperationStatus.Succeeded || store.State.Receipts.Single().Files.Length != 990)
    throw new InvalidOperationException("Deployment failed: " + JsonSerializer.Serialize(result));
foreach (var (name, hash) in hashes)
    if (BlobStore.Hash(Path.Combine(source, name)) != hash || BlobStore.Hash(Path.Combine(target, name)) != hash)
        throw new InvalidOperationException("File verification failed: " + name);
watch.Restart();
var clear = new DeploymentPlanner(store).Build(store.State, game.Id, [new(library.Id, null, OperationKind.Clear)]);
stages["clearPreflightSeconds"] = watch.Elapsed.TotalSeconds;
watch.Restart();
var cleared = new DeploymentEngine(store).Execute(clear, new(true, true));
stages["clearSeconds"] = watch.Elapsed.TotalSeconds;
if (cleared.Single().Status != OperationStatus.Succeeded || store.State.Receipts.Length != 0 || Directory.GetFiles(target).Length != 1)
    throw new InvalidOperationException("Clear verification failed.");
var report = new { revision = args[1], files = hashes.Count, bytes = 990L * data.Length,
    executableBytes = new FileInfo(exe).Length, stages, verified = true,
    scope = "Synthetic local SSD runner; real storage, antivirus and app process checks are not represented." };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));
