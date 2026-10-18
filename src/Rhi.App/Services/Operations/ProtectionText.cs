using System.Globalization;
using Rhi.App.Localization;
using Rhi.Core.Models;

namespace Rhi.App.Services.Operations;

internal static class ProtectionText
{
    // Resolve language resources on the UI thread; the engine receives a pure formatter.
    public static Func<ProtectionEntry, string> Capture()
    {
        var template = Strings.Get("ProtectionDescription");
        var missing = Strings.Get("SnapshotMissing");
        var modified = Strings.Get("SnapshotModified");
        var unchanged = Strings.Get("SnapshotUnchanged");
        var actions = new Dictionary<OperationKind, string>
        { [OperationKind.Change] = Strings.Get("Change"), [OperationKind.Clear] = Strings.Get("Clear") };
        var culture = CultureInfo.CurrentCulture;
        return entry => string.Format(culture, template, entry.GameName, entry.GameId, entry.LibraryName, entry.LibraryId,
            entry.VersionLabel, entry.VersionId, entry.TargetRoot, entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", culture),
            entry.OperationId, entry.Kind is { } kind ? actions.GetValueOrDefault(kind, kind.ToString()) : "",
            string.Join(Environment.NewLine, entry.Files.Select(file =>
                (file.BeforeHash is null ? missing : entry.ModifiedFiles?.Contains(file.RelativePath, StringComparer.OrdinalIgnoreCase) == true
                    ? modified : unchanged) + " | " + file.RelativePath + " | SHA-256: " + (file.BeforeHash ?? "—"))));
    }
}
