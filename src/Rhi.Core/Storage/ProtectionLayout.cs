using System.Globalization;
using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Storage;

// Only the browsable protection copies use this layout. Blobs and recovery journals do not.
public static class ProtectionLayout
{
    public static string CreateDirectory(ProtectionEntry entry)
    {
        var time = entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss_zzz", CultureInfo.InvariantCulture)
            .Replace(':', '-');
        return Path.Combine(Label(entry.GameName, entry.GameId), Label(entry.LibraryName, entry.LibraryId),
            time + "__" + entry.OperationId.ToString("N"));
    }

    public static string FilesDirectory(string protectionRoot, ProtectionEntry entry)
    {
        // Missing field means a pre-layout record: never relocate or rename old copies on read.
        var relative = entry.ProtectionDirectory ?? entry.OperationId.ToString("N");
        if (entry.ProtectionDirectory is not null)
        {
            relative = PathPolicy.Relative(relative);
            var parts = relative.Replace('\\', '/').Split('/');
            if (parts.Length != 3 || !parts[2].EndsWith("__" + entry.OperationId.ToString("N"), StringComparison.Ordinal))
                throw new DeploymentException("InspectionFailed", relative);
        }
        return PathPolicy.Target(protectionRoot, Path.Combine(relative, "files"));
    }

    private static string Label(string? name, Guid id)
    {
        var label = new string((name ?? "").Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray())
            .Trim().TrimEnd('.');
        if (label.Length > 40)
        {
            label = label[..40];
            if (char.IsHighSurrogate(label[^1])) label = label[..^1];
            label = label.TrimEnd(' ', '.');
        }
        if (label.Length == 0) return id.ToString("N");
        var stem = label.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             "0123456789¹²³".Contains(stem[3]))) label = "_" + label;
        return PathPolicy.Relative(label);
    }
}
