using System.Security.Cryptography;
using Rhi.Core.Models;
using Rhi.Core.Storage.Windows;

namespace Rhi.Core.Deployment;

// Windows enforces this read-only sharing contract for the entire operation, including its parent path.
internal sealed class ExecutableLease : IDisposable
{
    private readonly DirectoryLease parent;
    private readonly FileStream stream;
    private readonly string path;
    private readonly string hash;
    public ExecutableLease(string path, string expectedHash)
    {
        this.path = path;
        parent = DirectoryLease.Acquire(Path.GetDirectoryName(path)!);
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                hash = Convert.ToHexString(SHA256.HashData(stream));
                if (hash != expectedHash) throw new DeploymentException("GameIdentityChanged", path);
            }
            catch { stream.Dispose(); throw; }
        }
        catch { parent.Dispose(); throw; }
    }
    public bool Matches(string expectedPath, string expectedHash) => PathPolicy.Comparer.Equals(path, expectedPath) && hash == expectedHash;
    public void Dispose() { stream.Dispose(); parent.Dispose(); }
}
