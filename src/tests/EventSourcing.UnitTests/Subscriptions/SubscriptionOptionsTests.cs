namespace Purview.EventSourcing.Subscriptions;

public sealed class SubscriptionOptionsTests
{
	[Test]
	public async Task Validate_GivenDefaults_DoesNotThrow()
	{
		SubscriptionOptions options = new();

		await Assert.That(options.Validate).ThrowsNothing();
	}

	[Test]
	public async Task Validate_GivenANonPositivePollInterval_Throws()
	{
		SubscriptionOptions options = new() { PollInterval = TimeSpan.Zero };

		await Assert.That(options.Validate).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task Validate_GivenACheckpointIntervalBelowOne_Throws()
	{
		SubscriptionOptions options = new() { CheckpointInterval = 0 };

		await Assert.That(options.Validate).Throws<InvalidOperationException>();
	}

	// A cycle that re-read an earlier range must not rewind a position that is already further on,
	// which would redeliver everything between the two.
	[Test]
	public async Task SetAsync_GivenAnEarlierVersionThanRecorded_DoesNotMoveBackwards()
	{
		InMemorySubscriptionCheckpointStore checkpoints = new();
		SubscriptionCheckpointKey key = new("projection", Tenancy.TenantScope.System, "aggregate-1");

		await checkpoints.SetAsync(key, 50, CancellationToken.None);
		await checkpoints.SetAsync(key, 10, CancellationToken.None);

		await Assert.That(await checkpoints.GetAsync(key, CancellationToken.None)).IsEqualTo(50);
	}

	[Test]
	public async Task GetAsync_GivenAStreamNeverRead_ReturnsNull()
	{
		InMemorySubscriptionCheckpointStore checkpoints = new();
		SubscriptionCheckpointKey key = new("projection", Tenancy.TenantScope.System, "aggregate-1");

		await Assert.That(await checkpoints.GetAsync(key, CancellationToken.None)).IsNull();
	}
}
