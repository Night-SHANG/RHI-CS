using System.Security.Cryptography;
using Rhi.Core.Deployment;
using Rhi.Core.Deployment.Resources.ReShade;
using Rhi.Core.Models;
using Rhi.Core.Storage;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Preparation;

public sealed record OfflineReShadeRequest(string Installer, string Parent, string FolderName, string Architecture, string Api);

public static class OfflineReShade
{
    public static string Validate(WorkspaceState state, string dataRoot, OfflineReShadeRequest request)
    {
        var source = PathPolicy.Full(request.Installer);
        if (!File.Exists(source) || !Path.GetExtension(source).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new DeploymentException("ReShadeInvalidArchive", source);
        if (request.Architecture is not ("x86" or "x64")) throw new DeploymentException("ReShadeInvalidBinary");
        ReShadeAdapter.ProxyName(request.Api);
        var parent = PathPolicy.Full(request.Parent);
        if (!Directory.Exists(parent)) throw new DeploymentException("OfflineOutputInvalid", parent);
        var name = PathPolicy.Relative(request.FolderName.Trim());
        if (name.Length == 0 || name.IndexOfAny(['/', '\\']) >= 0) throw new DeploymentException("OfflineOutputInvalid");
        var output = PathPolicy.Target(parent, name);
        if (File.Exists(output) || Directory.Exists(output)) throw new DeploymentException("OfflineOutputExists", output);
        PathPolicy.Disjoint(source, output); PathPolicy.Disjoint(dataRoot, output);
        foreach (var game in state.Games)
        { PathPolicy.DisjointSource(game.InstallRoot, output); PathPolicy.DisjointSource(game.DeploymentRoot, output); }
        foreach (var version in state.Libraries.SelectMany(l => l.Versions)) PathPolicy.DisjointSource(version.SourcePath, output);
        return output;
    }

    public static string Create(WorkspaceState state, string dataRoot, OfflineReShadeRequest request)
    {
        var output = Validate(state, dataRoot, request);
        // Reuse the official appended-ZIP adapter and INI serializer; never execute the installer.
        var module = ReShadeAdapter.LoadModule(PathPolicy.Full(request.Installer), request.Architecture).Module;
        var ini = ReShadeConfiguration.Prepare(null, output).Content;
        using var parentLease = DirectoryLease.Acquire(request.Parent);
        Validate(state, dataRoot, request);
        var staging = PathPolicy.Target(parentLease.Identity.Path, ".rhi-offline-" + Guid.NewGuid().ToString("N") + ".partial");
        Directory.CreateDirectory(staging);
        using (var stagingLease = DirectoryLease.Acquire(staging))
        {
            Write(stagingLease.Identity.Path, ReShadeAdapter.ProxyName(request.Api), module);
            Write(stagingLease.Identity.Path, "ReShade.ini", ini);
        }
        // Publish only complete packages. An interrupted private staging folder is retained, never reported as success.
        Directory.Move(staging, Path.Combine(parentLease.Identity.Path, request.FolderName.Trim()));
        return output;
    }

    private static void Write(string directory, string name, byte[] bytes)
    {
        var file = PathPolicy.Target(directory, name);
        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        if (BlobStore.Hash(file) != Convert.ToHexString(SHA256.HashData(bytes)))
            throw new DeploymentException("VerificationFailed", file);
    }
}
