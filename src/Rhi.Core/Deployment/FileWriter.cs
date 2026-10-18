using Rhi.Core.Models;
using Rhi.Core.Storage;

namespace Rhi.Core.Deployment;

internal static class FileWriter
{
    public static string[] MissingDirectories(string root, IEnumerable<FileChange> changes)
    {
        var dirs = new HashSet<string>(PathPolicy.Comparer);
        foreach (var change in changes.Where(c => c.DesiredHash is not null))
        {
            var parent = Path.GetDirectoryName(PathPolicy.Target(root, change.RelativePath));
            while (parent is not null && !PathPolicy.Comparer.Equals(parent, root))
            {
                if (!Directory.Exists(parent)) dirs.Add(Path.GetRelativePath(root, parent));
                parent = Path.GetDirectoryName(parent);
            }
        }
        return dirs.OrderBy(d => d.Length).ToArray();
    }

    public static void CleanEmptyDirectories(string root, IEnumerable<string> directories)
    {
        foreach (var relative in directories.Distinct(PathPolicy.Comparer).OrderByDescending(d => d.Length))
        {
            var path = PathPolicy.Target(root, relative);
            if (PathPolicy.Comparer.Equals(path, root)) continue;
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
    }
}
