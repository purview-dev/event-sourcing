using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Fixtures.SqlServer;

namespace Purview.EventSourcing.Samples.Domain;

[ClassDataSource<SqlServerSnapshotEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class InventoryAggregateIntegrationTests(SqlServerSnapshotEventStoreFixture fixture)
{
	static InventoryAggregate CreateInitialized(
		string id,
		string productId = "prod-1",
		string productName = "Widget A",
		int qty = 100
	)
	{
		InventoryAggregate inv = new();
		inv.Details.Id = id;
		inv.Create(productId, productName, "loc-1", "Main Warehouse", initialQuantity: qty);
		return inv;
	}

	#region Round-Trip Persistence

	[Test]
	public async Task SaveAsync_GivenInitializedInventory_LoadedQuantitiesMatch(CancellationToken cancellationToken)
	{
		var inv = CreateInitialized($"{Guid.NewGuid()}", qty: 50);

		var store = fixture.CreateEventStore<InventoryAggregate>();
		await store.SaveAsync(inv, cancellationToken);

		var loadedResult = await store.GetAsync(inv.Id(), cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.ProductId).IsEqualTo("prod-1");
		await Assert.That(loaded.ProductName).IsEqualTo("Widget A");
		await Assert.That(loaded.QuantityOnHand).IsEqualTo(50);
		await Assert.That(loaded.ReservedQuantity).IsEqualTo(0);
		await Assert.That(loaded.AvailableQuantity).IsEqualTo(50);
	}

	[Test]
	public async Task SaveAsync_GivenInventoryWithStockReservation_LoadedReservedQuantityMatches(
		CancellationToken cancellationToken
	)
	{
		var inv = CreateInitialized($"{Guid.NewGuid()}", qty: 100);
		inv.ReserveStock(15, "order-1");

		var store = fixture.CreateEventStore<InventoryAggregate>();
		await store.SaveAsync(inv, cancellationToken);

		var loadedResult = await store.GetAsync(inv.Id(), cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.QuantityOnHand).IsEqualTo(100);
		await Assert.That(loaded.ReservedQuantity).IsEqualTo(15);
		await Assert.That(loaded.AvailableQuantity).IsEqualTo(85);
	}

	[Test]
	public async Task SaveAsync_GivenMultipleStockOperations_LoadedStateReflectsAllOperations(
		CancellationToken cancellationToken
	)
	{
		var inv = CreateInitialized($"{Guid.NewGuid()}", qty: 100);
		inv.ReserveStock(10, "order-1");
		inv.ShipStock(10, "order-1");
		inv.ReceiveStock(50);

		var store = fixture.CreateEventStore<InventoryAggregate>();
		await store.SaveAsync(inv, cancellationToken);

		var loadedResult = await store.GetAsync(inv.Id(), cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.QuantityOnHand).IsEqualTo(140);
		await Assert.That(loaded.ReservedQuantity).IsEqualTo(0);
		await Assert.That(loaded.AvailableQuantity).IsEqualTo(140);
	}

	[Test]
	public async Task SaveAsync_GivenStockAdjustment_LoadedQuantityReflectsAdjustment(
		CancellationToken cancellationToken
	)
	{
		var inv = CreateInitialized($"{Guid.NewGuid()}", qty: 100);
		inv.ReserveStock(30, "order-2");
		inv.AdjustStock(20, "Physical count"); // reserves capped to 20

		var store = fixture.CreateEventStore<InventoryAggregate>();
		await store.SaveAsync(inv, cancellationToken);

		var loadedResult = await store.GetAsync(inv.Id(), cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.QuantityOnHand).IsEqualTo(20);
		await Assert.That(loaded.ReservedQuantity).IsEqualTo(20);
		await Assert.That(loaded.AvailableQuantity).IsEqualTo(0);
	}

	[Test]
	public async Task SaveAsync_GivenReleasedReservation_LoadedReservedQuantityIsZero(
		CancellationToken cancellationToken
	)
	{
		var inv = CreateInitialized($"{Guid.NewGuid()}", qty: 100);
		inv.ReserveStock(25, "order-3");
		inv.ReleaseReservation(25, "order-3");

		var store = fixture.CreateEventStore<InventoryAggregate>();
		await store.SaveAsync(inv, cancellationToken);

		var loadedResult = await store.GetAsync(inv.Id(), cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.ReservedQuantity).IsEqualTo(0);
		await Assert.That(loaded.AvailableQuantity).IsEqualTo(100);
	}

	#endregion

	#region Version Tracking

	[Test]
	public async Task SaveAsync_GivenMultipleOperations_VersionIsTrackedCorrectly(CancellationToken cancellationToken)
	{
		var inv = CreateInitialized($"{Guid.NewGuid()}", qty: 100); // v1
		inv.ReceiveStock(50); // v2
		inv.ReserveStock(10, "order-x"); // v3

		var store = fixture.CreateEventStore<InventoryAggregate>();
		await store.SaveAsync(inv, cancellationToken);

		var loadedResult = await store.GetAsync(inv.Id(), cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.Details.SavedVersion).IsEqualTo(3);
		await Assert.That(loaded.Details.CurrentVersion).IsEqualTo(3);
	}

	#endregion

	#region Point-in-Time Replay

	[Test]
	public async Task GetAtAsync_GivenInventoryAfterReceiveStock_ReturnsStateBeforeReceive(
		CancellationToken cancellationToken
	)
	{
		var id = $"{Guid.NewGuid()}";
		var inv = CreateInitialized(id, qty: 100); // v1
		inv.ReceiveStock(50); // v2

		var store = fixture.CreateEventStore<InventoryAggregate>();
		await store.SaveAsync(inv, cancellationToken);

		var atV1Result = await store.GetAtAsync(id, 1, cancellationToken);
		var atV1 = atV1Result.IsSuccess ? atV1Result.Value : null;

		await Assert.That(atV1).IsNotNull();
		await Assert.That(atV1!.QuantityOnHand).IsEqualTo(100);
		await Assert.That(atV1.Details.CurrentVersion).IsEqualTo(1);
	}

	#endregion

	#region Event Replay Without Snapshots

	[Test]
	public async Task SaveAsync_GivenInventoryWithNoSnapshot_EventReplayRestoresState(
		CancellationToken cancellationToken
	)
	{
		var inv = CreateInitialized($"{Guid.NewGuid()}", qty: 200);
		inv.ReserveStock(50, "order-snap");
		inv.ShipStock(50, "order-snap");
		inv.ReceiveStock(100);

		var store = fixture.CreateSnapshotStore(snapshotStrategy: new AlwaysSnapshotStrategy<InventoryAggregate>());
		await store.SaveAsync(inv, cancellationToken);

		var loadedResult = await store.GetAsync(inv.Id(), cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.QuantityOnHand).IsEqualTo(250);
		await Assert.That(loaded.ReservedQuantity).IsEqualTo(0);
		await Assert.That(loaded.Details.SavedVersion).IsEqualTo(4);
	}

	#endregion
}
