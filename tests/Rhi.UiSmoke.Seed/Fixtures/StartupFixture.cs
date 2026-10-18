using Rhi.App.Services;
using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.UiSmoke.Seed;

internal static class StartupFixture
{
    private sealed record Context(string AppRoot, DataPointer Original, DataPointer Missing);

    public static void Prepare(string metadata, string appRoot)
    {
        appRoot = Path.GetFullPath(appRoot);
        var original = Bootstrap.Read() ?? throw new InvalidOperationException("Missing CI fixture.");
        if (Directory.Exists(Path.Combine(appRoot, "Data")))
            throw new InvalidOperationException("Unexpected application Data directory.");
        var missing = new DataPointer(Path.Combine(Path.GetTempPath(), "RhiTests", Guid.NewGuid().ToString("N"), "missing"), Guid.NewGuid());
        Bootstrap.Save(missing);
        var ledgerPath = Path.Combine(original.Root, "workspace.json");
        var ledger = File.ReadAllBytes(ledgerPath);
        try
        {
            using var unexpected = Bootstrap.StartNew(original.Root, missing);
            throw new InvalidOperationException("Existing data was accepted as new.");
        }
        catch (DeploymentException ex) when (ex.Code == "EmptyDataFolderRequired") { }
        if (Bootstrap.Read() != missing || !ledger.SequenceEqual(File.ReadAllBytes(ledgerPath)))
            throw new InvalidOperationException("Rejected restart changed the locator or old data.");
        AtomicJson.Write(metadata, new Context(appRoot, original, missing));
        Console.WriteLine("STARTUP: occupied target rejected; existing data and locator unchanged.");
    }

    public static void Verify(string metadata)
    {
        var context = AtomicJson.Read<Context>(metadata);
        var pointer = Bootstrap.Read() ?? throw new InvalidOperationException("Missing new locator.");
        var expectedRoot = Path.Combine(context.AppRoot, "Data");
        if (pointer.Root != expectedRoot || pointer.Id == context.Missing.Id || Directory.Exists(context.Missing.Root))
            throw new InvalidOperationException("Restart did not isolate new data.");
        using (var store = WorkspaceStore.Open(pointer.Root, expectedId: pointer.Id))
        {
            if (store.State.Games.Length != 0 || store.State.Libraries.Length != 0 || store.State.Receipts.Length != 0)
                throw new InvalidOperationException("New workspace is not empty.");
        }
        var history = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI-CS", "previous-locations");
        if (!Directory.EnumerateFiles(history, "*.json").Any(p => AtomicJson.Read<DataPointer>(p) == context.Missing))
            throw new InvalidOperationException("Previous locator was not preserved.");
        // Keep runner-only data for diagnosis, outside the downloadable application artifact.
        var retained = Path.Combine(Path.GetTempPath(), "RhiTests", "restart-" + Guid.NewGuid().ToString("N"));
        Directory.Move(expectedRoot, retained);
        Bootstrap.Save(context.Original);
        Console.WriteLine("STARTUP: new identity, empty records, archived locator and restart persistence verified; fixture retained outside application artifact.");
    }
}
