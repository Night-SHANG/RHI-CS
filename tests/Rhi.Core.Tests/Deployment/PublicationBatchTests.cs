using Rhi.Core.Deployment;

namespace Rhi.Core.Tests.Deployment;

[Collection("Publication flush concurrency")]
public sealed class PublicationBatchTests
{
    [Fact]
    public async Task Drain_limits_parallel_barriers_and_closes_every_handle_before_completion()
    {
        using var entered = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        using var batch = new PublicationBatch();
        var active = 0;
        var maximum = 0;
        var calls = 0;
        var completed = 0;
        var files = Enumerable.Range(0, 32).Select(_ => new FlushFile(durable =>
        {
            Assert.True(durable);
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximum, current);
            if (Interlocked.Increment(ref calls) <= 4) entered.Signal();
            try { Assert.True(release.Wait(TimeSpan.FromSeconds(20))); }
            finally { Interlocked.Decrement(ref active); }
        })).ToArray();
        for (var i = 0; i < files.Length; i++) batch.Add(files[i], i);
        var drain = Task.Run(() => batch.Drain(_ =>
        {
            Assert.All(files, file => Assert.True(file.Closed));
            Assert.Equal(32, calls);
            completed++;
        }));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(20)));
            Assert.Equal(4, Volatile.Read(ref active));
            Assert.False(drain.IsCompleted);
            Assert.Equal(0, completed);
            Assert.All(files, file => Assert.False(file.Closed));
        }
        finally { release.Set(); await drain; }
        await drain;
        Assert.Equal(4, maximum);
        Assert.Equal(32, completed);
        Assert.All(files, file => Assert.Equal(1, file.DurableFlushes));
    }

    [Fact]
    public async Task One_failed_flush_joins_outstanding_work_and_attempts_the_rest_before_release()
    {
        using var entered = new CountdownEvent(4);
        using var failed = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var batch = new PublicationBatch();
        var failure = new IOException("failed durable barrier");
        var completed = 0;
        var files = Enumerable.Range(0, 8).Select(index => new FlushFile(_ =>
        {
            if (index >= 4) return;
            entered.Signal();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(20)));
            if (index == 0) { failed.Set(); throw failure; }
            Assert.True(release.Wait(TimeSpan.FromSeconds(20)));
        })).ToArray();
        for (var i = 0; i < files.Length; i++) batch.Add(files[i], i);
        var drain = Task.Run(() => Record.Exception(() => batch.Drain(_ => completed++)));
        try
        {
            Assert.True(failed.Wait(TimeSpan.FromSeconds(20)));
            Assert.False(drain.IsCompleted);
            Assert.All(files, file => Assert.False(file.Closed));
            Assert.Equal(0, completed);
        }
        finally { release.Set(); await drain; }
        Assert.Same(failure, await drain);
        Assert.All(files, file => Assert.Equal(1, file.DurableFlushes));
        Assert.All(files, file => Assert.True(file.Closed));
        Assert.Equal(0, completed);
    }

    [Fact]
    public void Capacity_rejects_an_extra_handle_without_taking_ownership()
    {
        using var batch = new PublicationBatch();
        var files = Enumerable.Range(0, 32).Select(_ => new FlushFile(_ => { })).ToArray();
        for (var i = 0; i < files.Length; i++) batch.Add(files[i], i);
        using var extra = new FlushFile(_ => { });
        Assert.Throws<InvalidOperationException>(() => batch.Add(extra, 32));
        Assert.False(extra.Closed);
        batch.Dispose();
        Assert.All(files, file => Assert.True(file.Closed));
        Assert.All(files, file => Assert.Equal(0, file.DurableFlushes));
        Assert.False(extra.Closed);
    }

    [Fact]
    public void Postflush_fault_closes_the_entire_batch_without_reporting_completion()
    {
        using var batch = new PublicationBatch((phase, index) =>
        {
            if (phase == "TargetPublished" && index == 0) throw new IOException("postflush fixture");
        });
        var files = Enumerable.Range(0, 3).Select(_ => new FlushFile(_ => { })).ToArray();
        for (var i = 0; i < files.Length; i++) batch.Add(files[i], i);
        var completed = 0;
        Assert.Throws<IOException>(() => batch.Drain(_ => completed++));
        Assert.All(files, file => Assert.True(file.Closed));
        Assert.All(files, file => Assert.Equal(1, file.DurableFlushes));
        Assert.Equal(0, completed);
    }

    [Fact]
    public void Handle_close_failure_still_releases_other_handles_and_prevents_completion()
    {
        using var batch = new PublicationBatch();
        var first = new FlushFile(_ => { }, failOnClose: true);
        var second = new FlushFile(_ => { });
        batch.Add(first, 0);
        batch.Add(second, 1);
        var completed = 0;
        Assert.Throws<IOException>(() => batch.Drain(_ => completed++));
        Assert.True(first.Closed);
        Assert.True(second.Closed);
        Assert.Equal(1, first.DurableFlushes);
        Assert.Equal(1, second.DurableFlushes);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void No_publication_entries_complete_in_order_without_a_flush()
    {
        using var batch = new PublicationBatch((_, _) => throw new InvalidOperationException("unexpected flush hook"));
        batch.Add(null, 7);
        batch.Add(null, 8);
        var completed = new List<int>();
        batch.Drain(completed.Add);
        batch.Drain(completed.Add); // An empty tail does not repeat completion.
        Assert.Equal(new[] { 7, 8 }, completed);
    }

    [Fact]
    public void Sparse_publications_flush_once_without_skipping_null_slot_completions()
    {
        using var batch = new PublicationBatch();
        var first = new FlushFile(durable => Assert.True(durable));
        var last = new FlushFile(durable => Assert.True(durable));
        for (var i = 0; i < 32; i++) batch.Add(i == 1 ? first : i == 31 ? last : null, i);
        var completed = new List<int>();
        batch.Drain(index =>
        {
            Assert.True(first.Closed);
            Assert.True(last.Closed);
            completed.Add(index);
        });
        Assert.Equal(1, first.DurableFlushes);
        Assert.Equal(1, last.DurableFlushes);
        Assert.Equal(Enumerable.Range(0, 32), completed);
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        int previous;
        do { previous = Volatile.Read(ref maximum); }
        while (previous < value && Interlocked.CompareExchange(ref maximum, value, previous) != previous);
    }

    // Real disposable fixture files; only the OS durability boundary is intercepted
    // so failures and simultaneous in-flight barriers are reproducible, not timed.
    private sealed class FlushFile : FileStream
    {
        private readonly Action<bool> flushing;
        private readonly bool failOnClose;
        public bool Closed { get; private set; }
        public int DurableFlushes { get; private set; }

        public FlushFile(Action<bool> flushing, bool failOnClose = false) : base(NewPath(), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.Read)
        {
            this.flushing = flushing;
            this.failOnClose = failOnClose;
            WriteByte(42);
        }

        public override void Flush(bool flushToDisk)
        {
            if (flushToDisk) DurableFlushes++;
            flushing(flushToDisk);
            base.Flush(flushToDisk);
        }

        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally { if (disposing) Closed = true; }
            if (disposing && failOnClose) throw new IOException("close failure fixture");
        }

        private static string NewPath()
        {
            var root = Path.Combine(Path.GetTempPath(), "RhiTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return Path.Combine(root, "flush.bin");
        }
    }
}
