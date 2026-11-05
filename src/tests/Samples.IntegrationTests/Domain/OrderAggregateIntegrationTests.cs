using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.Samples.ValueObjects;

namespace Purview.EventSourcing.Samples.Domain;

[ClassDataSource<SqlServerSnapshotEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class OrderAggregateIntegrationTests(SqlServerSnapshotEventStoreFixture fixture)
{
	static OrderAggregate CreateDraftWithItems(string id)
	{
		OrderAggregate order = new();
		order.Details.Id = id;
		order
			.CreateOrder("customer-1")
			.AddLineItem("prod-1", "Widget A", 2, 29.99m)
			.AddLineItem("prod-2", "Widget B", 1, 49.99m);

		return order;
	}

	#region Round-Trip Persistence

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506:Avoid excessive class coupling",
		Justification = "Test wiring: the store, its infrastructure and the result model are all named here."
	)]
	public async Task SaveAsync_GivenDraftOrderWithLineItems_LoadedLineItemsMatch(CancellationToken cancellationToken)
	{
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id);

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		var loadedResult = await store.GetAsync(id, cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.CustomerId).IsEqualTo("customer-1");
		await Assert.That(loaded.Status).IsEqualTo(OrderStatusCode.Draft);
		await Assert.That(loaded.LineItems).Count().IsEqualTo(2);
		await Assert.That(loaded.TotalAmount).IsEqualTo(109.97m);
		await Assert.That(loaded.LineItems.ElementAt(0).ProductId).IsEqualTo("prod-1");
		await Assert.That(loaded.LineItems.ElementAt(0).Quantity).IsEqualTo(2);
		await Assert.That(loaded.LineItems.ElementAt(1).ProductId).IsEqualTo("prod-2");
		await Assert.That(loaded.LineItems.ElementAt(1).UnitPrice).IsEqualTo(49.99m);
	}

	[Test]
	public async Task SaveAsync_GivenDraftOrderWithNullableNotes_LoadedNotesMatch(CancellationToken cancellationToken)
	{
		var id = $"{Guid.NewGuid()}";
		OrderAggregate order = new();
		order.Details.Id = id;
		order.CreateOrder("customer-2");
		order.AddLineItem("prod-1", "Widget", 1, 10m);
		order.UpdateNotes("Rush delivery");

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		var loadedResult = await store.GetAsync(id, cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.Notes).IsEqualTo("Rush delivery");
	}

	[Test]
	public async Task SaveAsync_GivenConfirmedOrder_LoadedStatusIsConfirmed(CancellationToken cancellationToken)
	{
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id);
		order.ConfirmOrder();

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		var loadedResult = await store.GetAsync(id, cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.Status).IsEqualTo(OrderStatus.Confirmed);
	}

	[Test]
	public async Task SaveAsync_GivenShippedOrder_LoadedShippedAtAndStatusMatch(CancellationToken cancellationToken)
	{
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id);
		order.SetShippingAddress("123 Main St");
		order.ConfirmOrder();
		order.ShipOrder();

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		var loadedResult = await store.GetAsync(id, cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.Status).IsEqualTo(OrderStatus.Shipped);
		await Assert.That(loaded.ShippedAt).IsNotNull();
		await Assert.That(loaded.ShippingAddress).IsEqualTo("123 Main St");
	}

	[Test]
	public async Task SaveAsync_GivenFullLifecycleOrder_LoadedStateMatchesFinal(CancellationToken cancellationToken)
	{
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id);
		order.SetShippingAddress("456 Commerce Ave");
		order.ConfirmOrder();
		order.ShipOrder();
		order.CompleteOrder();

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		var loadedResult = await store.GetAsync(id, cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.Status).IsEqualTo(OrderStatus.Completed);
		await Assert.That(loaded.CompletedAt).IsNotNull();
		await Assert.That(loaded.TotalAmount).IsEqualTo(109.97m);
		await Assert.That(loaded.LineItems).Count().IsEqualTo(2);
	}

	[Test]
	public async Task SaveAsync_GivenCancelledOrder_LoadedStatusIsCancelled(CancellationToken cancellationToken)
	{
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id);
		order.ConfirmOrder();
		order.CancelOrder();

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		var loadedResult = await store.GetAsync(id, cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.Status).IsEqualTo(OrderStatus.Cancelled);
	}

	[Test]
	public async Task SaveAsync_GivenOrderWithRemovedLineItem_LoadedLineItemsReflectRemoval(
		CancellationToken cancellationToken
	)
	{
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id); // 2 items
		order.RemoveLineItem("prod-1");

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		var loadedResult = await store.GetAsync(id, cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.LineItems).Count().IsEqualTo(1);
		await Assert.That(loaded.LineItems.ElementAt(0).ProductId).IsEqualTo("prod-2");
		await Assert.That(loaded.TotalAmount).IsEqualTo(49.99m);
	}

	#endregion

	#region Version Tracking

	[Test]
	public async Task SaveAsync_GivenFullLifecycleOrder_VersionMatchesEventCount(CancellationToken cancellationToken)
	{
		// CreateOrder + Add + Add + SetAddr + Confirm + Ship + Complete = 7 events
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id);
		order.SetShippingAddress("789 Oak St");
		order.ConfirmOrder();
		order.ShipOrder();
		order.CompleteOrder();

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		var loadedResult = await store.GetAsync(id, cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.Details.SavedVersion).IsEqualTo(7);
		await Assert.That(loaded.Details.CurrentVersion).IsEqualTo(7);
	}

	#endregion

	#region Point-in-Time Replay

	[Test]
	public async Task GetAtAsync_GivenOrderAtVersion1_ReturnsDraftStatusWithNoItems(CancellationToken cancellationToken)
	{
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id); // v1=Created, v2=AddItem, v3=AddItem
		order.ConfirmOrder(); // v4

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		// At version 1, only CreateOrder has been applied
		var atV1Result = await store.GetAtAsync(id, 1, cancellationToken);
		var atV1 = atV1Result.IsSuccess ? atV1Result.Value : null;

		await Assert.That(atV1).IsNotNull();
		await Assert.That(atV1!.Status).IsEqualTo(OrderStatus.Draft);
		await Assert.That(atV1.LineItems).IsEmpty();
		await Assert.That(atV1.Details.CurrentVersion).IsEqualTo(1);
	}

	[Test]
	public async Task GetAtAsync_GivenOrderBeforeConfirmation_ReturnsUnconfirmedState(
		CancellationToken cancellationToken
	)
	{
		// v1=Created, v2=AddItem(prod-1), v3=AddItem(prod-2), v4=Confirm
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id);
		order.ConfirmOrder();

		var store = fixture.CreateEventStore<OrderAggregate>();
		await store.SaveAsync(order, cancellationToken);

		// At version 3 (after two AddLineItem), status is still Draft
		var atV3Result = await store.GetAtAsync(id, 3, cancellationToken);
		var atV3 = atV3Result.IsSuccess ? atV3Result.Value : null;

		await Assert.That(atV3).IsNotNull();
		await Assert.That(atV3!.Status).IsEqualTo(OrderStatus.Draft);
		await Assert.That(atV3.LineItems).Count().IsEqualTo(2);
	}

	#endregion

	#region Event Replay Without Snapshots

	[Test]
	public async Task SaveAsync_GivenOrderWithNoSnapshot_EventReplayRestoresLineItems(
		CancellationToken cancellationToken
	)
	{
		var id = $"{Guid.NewGuid()}";
		var order = CreateDraftWithItems(id);
		order.SetShippingAddress("1 Event Replay Rd");
		order.ConfirmOrder();

		var store = fixture.CreateSnapshotStore(snapshotStrategy: new AlwaysSnapshotStrategy<OrderAggregate>());
		await store.SaveAsync(order, cancellationToken);

		var loadedResult = await store.GetAsync(id, cancellationToken);
		var loaded = loadedResult.IsSuccess ? loadedResult.Value : null;

		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.Status).IsEqualTo(OrderStatus.Confirmed);
		await Assert.That(loaded.LineItems).Count().IsEqualTo(2);
		await Assert.That(loaded.TotalAmount).IsEqualTo(109.97m);
	}

	#endregion
}
