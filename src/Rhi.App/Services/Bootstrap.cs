using Rhi.Core.Deployment;
using Rhi.Core.Storage;

namespace Rhi.App.Services;

public sealed record DataPointer(string Root, Guid Id);
public static class Bootstrap
{
    // Only the locator lives here. Catalogs, logs, snapshots and protection stay in the user-selected root.
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI-CS");
    public static DataPointer? Read()
    {
        var path = Path.Combine(Folder, "bootstrap.json");
        PathPolicy.CheckNoLinks(path);
        return File.Exists(path) ? AtomicJson.Read<DataPointer>(path) : null;
    }
    public static void Save(DataPointer pointer)
    {
        PathPolicy.CheckNoLinks(Folder);
        Directory.CreateDirectory(Folder);
        AtomicJson.Write(Path.Combine(Folder, "bootstrap.json"), pointer);
    }
}
