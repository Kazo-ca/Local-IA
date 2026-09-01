using LocalIA.Infrastructure.Router;

namespace LocalIA.Tests;

public class RouterConnectionTrackerTests
{
    [Fact]
    public void TryBeginEviction_ReturnsFalse_ForUntrackedTier()
    {
        var tracker = new RouterConnectionTracker();

        Assert.False(tracker.TryBeginEviction(Guid.NewGuid(), TimeSpan.Zero));
    }

    [Fact]
    public void TryBeginEviction_ReturnsFalse_WhileConnectionActive()
    {
        var tracker = new RouterConnectionTracker();
        var tierId = Guid.NewGuid();
        using var lease = tracker.BeginLease(tierId);

        Assert.False(tracker.TryBeginEviction(tierId, TimeSpan.Zero));
    }

    [Fact]
    public void TryBeginEviction_ReturnsTrue_OnceIdle_AndMarksEvictionInProgress()
    {
        var tracker = new RouterConnectionTracker();
        var tierId = Guid.NewGuid();
        tracker.BeginLease(tierId).Dispose();

        Assert.True(tracker.TryBeginEviction(tierId, TimeSpan.Zero));
        // Un second appel concurrent ne doit pas décider deux fois la même éviction.
        Assert.False(tracker.TryBeginEviction(tierId, TimeSpan.Zero));

        tracker.EndEviction(tierId);
        Assert.True(tracker.TryBeginEviction(tierId, TimeSpan.Zero));
    }

    [Fact]
    public void TryBeginEviction_ReturnsFalse_WhenNotIdleLongEnough()
    {
        var tracker = new RouterConnectionTracker();
        var tierId = Guid.NewGuid();
        tracker.BeginLease(tierId).Dispose();

        // Vient tout juste de se libérer — pas encore inactif depuis 1 heure.
        Assert.False(tracker.TryBeginEviction(tierId, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void BeginLease_Reentrant_ActiveCountReflectsConcurrentRequests()
    {
        var tracker = new RouterConnectionTracker();
        var tierId = Guid.NewGuid();

        using (tracker.BeginLease(tierId))
        {
            using (tracker.BeginLease(tierId))
            {
                Assert.Equal(2, tracker.GetActiveCount(tierId));
            }

            Assert.Equal(1, tracker.GetActiveCount(tierId));
        }

        Assert.Equal(0, tracker.GetActiveCount(tierId));
    }
}
