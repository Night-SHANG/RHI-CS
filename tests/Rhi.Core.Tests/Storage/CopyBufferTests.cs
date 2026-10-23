using System.Buffers;
using System.Security.Cryptography;
using Rhi.Core.Storage;

namespace Rhi.Core.Tests.Storage;

public sealed class CopyBufferTests
{
    [Theory]
    [InlineData(131071)]
    [InlineData(131072)]
    [InlineData(131073)]
    [InlineData(400000)]
    public void Pooled_copy_hashes_only_written_bytes_and_returns_the_buffer(int length)
    {
        var content = new byte[length];
        new Random(42).NextBytes(content);
        var pool = new TrackingPool();
        using var input = new MemoryStream(content);
        using var output = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        CopyBuffer.CopyAndHash(input, output, hash, () => { }, pool);
        Assert.Equal(content, output.ToArray());
        Assert.Equal(SHA256.HashData(content), hash.GetHashAndReset());
        Assert.Equal(1, pool.Rented);
        Assert.Equal(1, pool.Returned);
        Assert.True(pool.Cleared);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Failed_copy_returns_the_pool_buffer_before_a_later_copy(int failOnBlock)
    {
        var pool = new TrackingPool();
        using var input = new MemoryStream(new byte[400000]);
        using var output = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var blocks = 0;
        Assert.Throws<IOException>(() => CopyBuffer.CopyAndHash(input, output, hash,
            () => { if (++blocks == failOnBlock) throw new IOException("fixture write failure"); }, pool));
        Assert.Equal(1, pool.Returned);
        using var nextInput = new MemoryStream([1, 2, 3]);
        using var nextOutput = new MemoryStream();
        using var nextHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        CopyBuffer.CopyAndHash(nextInput, nextOutput, nextHash, () => { }, pool);
        Assert.Equal(new byte[] { 1, 2, 3 }, nextOutput.ToArray());
        Assert.Equal(2, pool.Rented);
        Assert.Equal(2, pool.Returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Input_and_output_errors_return_the_buffer(bool readError)
    {
        var pool = new TrackingPool();
        using Stream input = readError ? new FailingStream(true) : new MemoryStream(new byte[8]);
        using Stream output = readError ? new MemoryStream() : new FailingStream(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Assert.Throws<IOException>(() => CopyBuffer.CopyAndHash(input, output, hash, () => { }, pool));
        Assert.Equal(1, pool.Rented);
        Assert.Equal(1, pool.Returned);
        Assert.True(pool.Cleared);
    }

    private sealed class FailingStream(bool readError) : MemoryStream(new byte[8])
    {
        public override int Read(byte[] buffer, int offset, int count) => readError
            ? throw new IOException("fixture read failure") : base.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!readError) throw new IOException("fixture write failure");
            base.Write(buffer, offset, count);
        }
    }

    private sealed class TrackingPool : ArrayPool<byte>
    {
        private readonly byte[] buffer = Enumerable.Repeat((byte)0xAA, 131100).ToArray();
        public int Rented { get; private set; }
        public int Returned { get; private set; }
        public bool Cleared { get; private set; }
        public override byte[] Rent(int minimumLength)
        {
            Assert.True(minimumLength <= buffer.Length);
            Assert.Equal(Rented, Returned);
            Rented++;
            return buffer;
        }
        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.Same(buffer, array);
            Returned++;
            Cleared = clearArray;
            if (clearArray) Array.Clear(array);
        }
    }
}
