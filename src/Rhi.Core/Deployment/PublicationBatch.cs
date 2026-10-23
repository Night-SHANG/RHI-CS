using System.Runtime.ExceptionServices;

namespace Rhi.Core.Deployment;

// Owns only newly published staging handles, never the parked originals. The
// caller drains before opening another staging file when the bounded batch fills.
internal sealed class PublicationBatch(Action<string, int>? fault = null) : IDisposable
{
    private const int Capacity = 32;
    private const int FlushConcurrency = 4;
    private readonly List<Entry> entries = new(Capacity);
    private readonly record struct Entry(FileStream? File, int Index);

    public bool IsFull => entries.Count == Capacity;

    // Ownership transfers only when Add returns. Null entries retain completion
    // order for clear/no-op files without opening a handle or adding a barrier.
    public void Add(FileStream? file, int index)
    {
        if (IsFull) throw new InvalidOperationException("Publication batch is full.");
        entries.Add(new(file, index));
    }

    public void Drain(Action<int> completed)
    {
        var pending = entries.ToArray();
        ExceptionDispatchInfo? failure = null;
        try
        {
            FlushAll(pending);
            // Same per-file hook state as before: durability has succeeded and the
            // published handle is still owned. Now all barriers in this batch have
            // joined, and these callbacks remain serial rather than worker calls.
            foreach (var entry in pending)
                if (entry.File is not null) fault?.Invoke("TargetPublished", entry.Index);
        }
        catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        try { Dispose(); }
        catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
        failure?.Throw();
        // AfterWrite must remain after handle release for external-change recovery
        // fixtures. Do not rewind the engine's last-applied journal index here.
        foreach (var entry in pending) completed(entry.Index);
    }

    private void FlushAll(Entry[] pending)
    {
        var workerCount = Math.Min(FlushConcurrency, pending.Count(entry => entry.File is not null));
        if (workerCount == 0) return;
        var failures = new ExceptionDispatchInfo?[pending.Length];
        var workers = new List<Task>(workerCount);
        ExceptionDispatchInfo? failure = null;
        try
        {
            for (var worker = 0; worker < workerCount; worker++)
            {
                var first = worker;
                workers.Add(Task.Run(() =>
                {
                    for (var slot = first; slot < pending.Length; slot += workerCount)
                    {
                        var entry = pending[slot];
                        if (entry.File is null) continue;
                        try
                        {
                            // Only these two fault hooks run concurrently. Each
                            // actual file still needs its own successful OS barrier.
                            fault?.Invoke("PublishFlushing", entry.Index);
                            entry.File.Flush(flushToDisk: true);
                            fault?.Invoke("PublishFlushed", entry.Index);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                        {
                            failures[slot] = ExceptionDispatchInfo.Capture(ex);
                        }
                    }
                }));
            }
        }
        catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        // Join even if creating a later worker failed. Never cancel outstanding
        // barriers or close their handles while they can still be used by a worker.
        foreach (var worker in workers)
        {
            try { worker.GetAwaiter().GetResult(); }
            catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
        }
        failure ??= failures.FirstOrDefault(error => error is not null);
        failure?.Throw();
    }

    public void Dispose()
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var entry in entries)
        {
            try { entry.File?.Dispose(); }
            catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
        }
        entries.Clear();
        failure?.Throw();
    }
}
