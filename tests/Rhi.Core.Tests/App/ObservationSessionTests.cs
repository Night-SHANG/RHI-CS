using Rhi.App.Services;
using Rhi.Core.Models;
using Rhi.Core.Tests.Deployment;

namespace Rhi.Core.Tests.App;

public sealed class ObservationSessionTests
{
    [Fact]
    public async Task Repeated_target_checks_publish_busy_and_idle_and_release_for_the_next_check()
    {
        using var f = new Sandbox();
        f.Deploy();
        var session = new AppSession(f.Store);
        var edges = new List<bool>();
        session.Changed += () => edges.Add(session.Busy);
        for (var i = 0; i < 3; i++)
        {
            var observation = await session.InspectTargets(f.Game.Id);
            Assert.Equal(f.Game.Id, observation.GameId);
            Assert.Single(observation.Receipts);
            Assert.False(session.Busy);
        }
        Assert.Equal(new[] { true, false, true, false, true, false }, edges);
    }

    [Fact]
    public async Task Reentrant_target_check_is_rejected_without_releasing_the_owners_busy_state()
    {
        using var f = new Sandbox();
        var session = new AppSession(f.Store);
        Task? reentrant = null;
        session.Changed += () =>
        {
            if (!session.Busy) return;
            reentrant = session.InspectTargets(f.Game.Id);
            Assert.True(session.Busy);
        };
        await session.InspectTargets(f.Game.Id);
        var error = await Assert.ThrowsAsync<DeploymentException>(() => reentrant!);
        Assert.Equal("Busy", error.Code);
        Assert.False(session.Busy);
    }

    [Fact]
    public async Task Worker_error_publishes_idle_and_allows_another_operation()
    {
        using var f = new Sandbox();
        var session = new AppSession(f.Store);
        var edges = new List<bool>();
        session.Changed += () => edges.Add(session.Busy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InspectTargets(Guid.NewGuid()));
        Assert.False(session.Busy);
        await session.InspectTargets(f.Game.Id);
        Assert.Equal(new[] { true, false, true, false }, edges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_or_cancellation_from_start_notification_cannot_strand_busy(bool cancelled)
    {
        using var f = new Sandbox();
        var session = new AppSession(f.Store);
        void Fail()
        {
            if (!session.Busy) return;
            if (cancelled) throw new OperationCanceledException();
            throw new InvalidOperationException("UI notification failed");
        }
        session.Changed += Fail;
        if (cancelled) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.InspectTargets(f.Game.Id));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => session.InspectTargets(f.Game.Id));
        Assert.False(session.Busy);
        session.Changed -= Fail;
        await session.InspectTargets(f.Game.Id);
        Assert.False(session.Busy);
    }

    [Fact]
    public async Task Protection_read_also_publishes_its_busy_lifetime()
    {
        using var f = new Sandbox();
        var session = new AppSession(f.Store);
        var edges = new List<bool>();
        session.Changed += () => edges.Add(session.Busy);
        await session.InspectProtection();
        Assert.Equal(new[] { true, false }, edges);
        Assert.False(session.Busy);
    }
}
