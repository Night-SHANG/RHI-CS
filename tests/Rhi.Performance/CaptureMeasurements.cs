using Rhi.Core.Storage;

// This adapter deliberately has no dependency on the new Core API unless the
// maintenance-only coordinator opts in. The historic f26 comparison still builds.
sealed class CaptureMeasurements : IDisposable
{
    private readonly BlobStore store;
    private readonly CaptureSummary result = new();
    private bool disposed;

    public static void RequireAvailable()
    {
#if !RHI_CAPTURE_METRICS
        throw new InvalidOperationException("Backup suite requires RHI_CAPTURE_METRICS and the audited measurement-only baseline overlay.");
#endif
    }

    public static CaptureMeasurements Attach(BlobStore store)
    {
        RequireAvailable();
        return new(store);
    }

    private CaptureMeasurements(BlobStore store)
    {
        this.store = store;
#if RHI_CAPTURE_METRICS
        if (store.CaptureObserver is not null) throw new InvalidOperationException("Capture observer already installed.");
        store.CaptureObserver = measurement =>
        {
            result.Calls++;
            if (measurement.Succeeded) result.SucceededCalls++;
            result.Seconds += measurement.ElapsedSeconds;
            result.SourceBytesRead += measurement.SourceBytesRead;
            result.BackupBytesRead += measurement.BackupBytesRead;
            result.BytesWritten += measurement.BytesWritten;
            result.BytesHashed += measurement.BytesHashed;
            result.HashPasses += measurement.HashPasses;
            result.FlushCalls += measurement.FlushCalls;
            result.PublishedBlobs += measurement.PublishedBlobs;
            result.ReusedBlobs += measurement.ReusedBlobs;
            if (measurement.Succeeded && measurement.PublishedBlobs > 0 && measurement.FlushCalls < 1)
                result.PublishedCallsWithoutDurableFlush++;
            if (measurement.WriteThrough) result.WriteThroughCalls++;
            result.CopySeconds += measurement.CopySeconds;
            result.VerificationSeconds += measurement.VerificationSeconds;
            result.DurableFlushSeconds += measurement.DurableFlushSeconds;
            result.PublishSeconds += measurement.PublishSeconds;
        };
#endif
    }

    public CaptureSummary Snapshot()
    {
        if (!disposed) throw new InvalidOperationException("Stop observing before reading counters.");
        return result;
    }

    public void Dispose()
    {
        if (disposed) return;
#if RHI_CAPTURE_METRICS
        store.CaptureObserver = null;
#endif
        disposed = true;
    }
}

sealed class CaptureSummary
{
    public int Calls { get; set; }
    public int SucceededCalls { get; set; }
    public double Seconds { get; set; }
    public long SourceBytesRead { get; set; }
    public long BackupBytesRead { get; set; }
    public long BytesWritten { get; set; }
    public long BytesHashed { get; set; }
    public int HashPasses { get; set; }
    public int FlushCalls { get; set; }
    public int PublishedBlobs { get; set; }
    public int ReusedBlobs { get; set; }
    public int PublishedCallsWithoutDurableFlush { get; set; }
    public int WriteThroughCalls { get; set; }
    public double CopySeconds { get; set; }
    public double VerificationSeconds { get; set; }
    public double DurableFlushSeconds { get; set; }
    public double PublishSeconds { get; set; }
    public double UnattributedSeconds => Seconds - CopySeconds - VerificationSeconds - DurableFlushSeconds - PublishSeconds;
}
