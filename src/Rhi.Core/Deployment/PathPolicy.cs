using Rhi.Core.Models;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Deployment;

public static class PathPolicy
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static string Full(string path)
    {
        var full = Absolute(path);
        CheckNoLinks(full);
        return full;
    }

    private static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path) || (path.StartsWith(@"\\", StringComparison.Ordinal) &&
            !path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)))
            throw new DeploymentException("LocalAbsolutePathRequired", path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static bool Within(string path, string root) => Comparer.Equals(path, root) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    public static void Disjoint(string first, string second)
    {
        first = DirectoryLease.PhysicalPath(Full(first));
        second = DirectoryLease.PhysicalPath(Full(second));
        if (Within(first, second) || Within(second, first))
            throw new DeploymentException("OverlappingPaths", $"{first} | {second}");
    }

    public static void DisjointSource(string source, string other)
    {
        var first = Absolute(source);
        var second = Full(other);
        if (Within(first, second) || Within(second, first))
            throw new DeploymentException("OverlappingPaths", $"{first} | {second}");
        // An entirely disconnected source volume cannot be a destination on an available volume.
        // Missing files on an available drive still undergo physical alias resolution.
        if (!Directory.Exists(Path.GetPathRoot(first))) return;
        Disjoint(first, second);
    }

    public static string Relative(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (Path.IsPathRooted(value)) throw new DeploymentException("UnsafeRelativePath", value);
        var parts = value.Replace('\\', '/').Split('/');
        foreach (var part in parts)
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
                stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
                throw new DeploymentException("UnsafeRelativePath", value);
        }
        return Path.Combine(parts);
    }

    public static string Target(string root, string relative)
    {
        root = Full(root);
        relative = Relative(relative);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!Within(path, root)) throw new DeploymentException("UnsafeRelativePath", relative);
        CheckNoLinks(path);
        return path;
    }

    public static void CheckNoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                var attr = File.GetAttributes(current);
                if ((attr & FileAttributes.ReparsePoint) != 0)
                    throw new DeploymentException("ReparsePoint", current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    public static IEnumerable<string> Files(string directory)
    {
        CheckNoLinks(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(Comparer))
        {
            CheckNoLinks(entry);
            if (Directory.Exists(entry))
                foreach (var child in Files(entry)) yield return child;
            else yield return entry;
        }
    }
}
