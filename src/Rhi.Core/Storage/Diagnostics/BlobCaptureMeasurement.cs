namespace Rhi.Core.Storage;

public sealed record BlobCaptureMeasurement(double ElapsedSeconds, long SourceBytesRead,
    long BackupBytesRead, long BytesWritten, long BytesHashed, int HashPasses, int FlushCalls,
    int PublishedBlobs, int ReusedBlobs, bool Succeeded, bool WriteThrough, double CopySeconds,
    double VerificationSeconds, double DurableFlushSeconds, double PublishSeconds);
