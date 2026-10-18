using System.Security.Cryptography;
using Rhi.Core.Deployment;
using Rhi.Core.Models;

namespace Rhi.Core.Storage;

public sealed class BlobStore(string root)
{
    public string PathFor(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid blob id");
        var path = Path.Combine(root, "blobs", hash);
        PathPolicy.CheckNoLinks(path);
        return path;
    }

    public static string? Hash(string path)
    {
        PathPolicy.CheckNoLinks(path);
        if (Directory.Exists(path)) throw new DeploymentException("FileDirectoryConflict", path);
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public void Verify(string hash)
    {
        if (Hash(PathFor(hash)) != hash) throw new DeploymentException("BackupUnavailable", hash);
    }

    public void CaptureGenerated(byte[] content, string expectedHash)
    {
        if (Convert.ToHexString(SHA256.HashData(content)) != expectedHash)
            throw new DeploymentException("ChangedSincePreflight");
        var dest = PathFor(expectedHash);
        if (File.Exists(dest)) { Verify(expectedHash); return; }
        var temp = dest + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.WriteThrough))
        {
            output.Write(content);
            output.Flush(true);
        }
        if (Hash(temp) != expectedHash) throw new DeploymentException("ChangedSincePreflight");
        File.Move(temp, dest);
        Verify(expectedHash);
    }

    public void Capture(string source, string expectedHash)
    {
        var dest = PathFor(expectedHash);
        if (File.Exists(dest)) { Verify(expectedHash); return; }
        var temp = dest + "." + Guid.NewGuid().ToString("N") + ".pending";
        PathPolicy.CheckNoLinks(source);
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.WriteThrough))
        {
            input.CopyTo(output);
            output.Flush(true);
        }
        if (Hash(temp) != expectedHash || Hash(source) != expectedHash)
            throw new DeploymentException("ChangedSincePreflight", source);
        File.Move(temp, dest);
        Verify(expectedHash);
    }
}
