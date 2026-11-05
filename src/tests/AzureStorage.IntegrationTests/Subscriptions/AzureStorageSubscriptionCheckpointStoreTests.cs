using Microsoft.Extensions.Options;
using Purview.EventSourcing.Fixtures.AzureStorage;
using Purview.EventSourcing.Subscriptions;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.AzureStorage.Subscriptions;

/// <summary>
/// The durable checkpoint store against real Azure Table Storage.
/// </summary>
/// <remarks>
/// Tenant separation is first: the whole point of a durable position is that it survives a restart,
/// and a position shared between tenants would survive as a permanent gap - whichever tenant ran
/// first advances it past the other's unread events, which are then never delivered.
/// </remarks>
[ClassDataSource<TableEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class AzureStorageSubscriptionCheckpointStoreTests(TableEventStoreFixture fixture)
{
	[Test]
	public async Task SetAsync_GivenTwoTenants_KeepsSeparatePositions(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var aggregateId = $"{Guid.NewGuid():N}";

		SubscriptionCheckpointKey acme = new("projection", TenantScope.For("acme"), aggregateId);
		SubscriptionCheckpointKey globex = new("projection", TenantScope.For("globex"), aggregateId);

		await store.SetAsync(acme, 50, cancellationToken);

		await Assert.That(await store.GetAsync(acme, cancellationToken)).IsEqualTo(50);
		await Assert.That(await store.GetAsync(globex, cancellationToken)).IsNull();
	}

	[Test]
	public async Task SetAsync_GivenTheSystemScope_IsSeparateFromEveryTenant(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var aggregateId = $"{Guid.NewGuid():N}";

		SubscriptionCheckpointKey system = new("projection", TenantScope.System, aggregateId);
		SubscriptionCheckpointKey acme = new("projection", TenantScope.For("acme"), aggregateId);

		await store.SetAsync(system, 50, cancellationToken);

		await Assert.That(await store.GetAsync(system, cancellationToken)).IsEqualTo(50);
		await Assert.That(await store.GetAsync(acme, cancellationToken)).IsNull();
	}

	[Test]
	public async Task SetAsync_GivenTwoSubscriptionNames_KeepsSeparatePositions(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var aggregateId = $"{Guid.NewGuid():N}";

		SubscriptionCheckpointKey first = new("projection-a", TenantScope.System, aggregateId);
		SubscriptionCheckpointKey second = new("projection-b", TenantScope.System, aggregateId);

		await store.SetAsync(first, 50, cancellationToken);

		await Assert.That(await store.GetAsync(second, cancellationToken)).IsNull();
	}

	[Test]
	public async Task GetAsync_GivenAStreamNeverRead_ReturnsNull(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		SubscriptionCheckpointKey key = new("projection", TenantScope.System, $"{Guid.NewGuid():N}");

		await Assert.That(await store.GetAsync(key, cancellationToken)).IsNull();
	}

	[Test]
	public async Task SetAsync_GivenAHigherVersion_AdvancesThePosition(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		SubscriptionCheckpointKey key = new("projection", TenantScope.System, $"{Guid.NewGuid():N}");

		await store.SetAsync(key, 10, cancellationToken);
		await store.SetAsync(key, 20, cancellationToken);

		await Assert.That(await store.GetAsync(key, cancellationToken)).IsEqualTo(20);
	}

	// A cycle that re-read an earlier range must not rewind a position that is already further on,
	// which would redeliver everything in between.
	[Test]
	public async Task SetAsync_GivenALowerVersion_DoesNotMoveBackwards(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		SubscriptionCheckpointKey key = new("projection", TenantScope.System, $"{Guid.NewGuid():N}");

		await store.SetAsync(key, 50, cancellationToken);
		await store.SetAsync(key, 10, cancellationToken);

		await Assert.That(await store.GetAsync(key, cancellationToken)).IsEqualTo(50);
	}

	[Test]
	public async Task SetAsync_GivenConcurrentFirstWrites_RecordsOneOfThem(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		SubscriptionCheckpointKey key = new("projection", TenantScope.System, $"{Guid.NewGuid():N}");

		// Both writers race to create the same row; the loser's duplicate-key failure is swallowed
		// rather than surfaced, because a position behind redelivers and redelivery is safe.
		await Task.WhenAll(Enumerable.Range(1, 8).Select(version => store.SetAsync(key, version, cancellationToken)));

		var recorded = await store.GetAsync(key, cancellationToken);

		await Assert.That(recorded).IsNotNull();
		await Assert.That(recorded!.Value).IsGreaterThanOrEqualTo(1).And.IsLessThanOrEqualTo(8);
	}

	AzureStorageSubscriptionCheckpointStore CreateStore() =>
		new(
			Options.Create(
				new AzureStorageSubscriptionCheckpointStoreOptions
				{
					ConnectionString = fixture.ConnectionString,
					// Azure table names are alphanumeric and must start with a letter.
					TableName = $"Checkpoints{Guid.NewGuid():N}",
				}
			),
			Options.Create(new AzureStorageEventStoreOptions { ConnectionString = fixture.ConnectionString })
		);
}
