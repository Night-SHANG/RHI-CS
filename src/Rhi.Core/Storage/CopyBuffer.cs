using System.Buffers;
using System.Security.Cryptography;

namespace Rhi.Core.Storage;

internal static class CopyBuffer
{
    internal static void CopyAndHash(Stream input, Stream output, IncrementalHash hash,
        Action duringWrite, ArrayPool<byte> pool)
    {
        const int size = 128 * 1024;
        var buffer = pool.Rent(size);
        try
        {
            int count;
            while ((count = input.Read(buffer, 0, size)) != 0)
            {
                output.Write(buffer, 0, count);
                hash.AppendData(buffer, 0, count);
                duringWrite();
            }
        }
        finally { pool.Return(buffer, clearArray: true); }
    }
}
