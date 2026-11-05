using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.Samples.ValueObjects;

namespace Purview.EventSourcing.Samples.Domain;

/// <summary>
/// Integration tests demonstrating cross-aggregate business workflows with real persistence.
/// These tests verify that independent aggregates can be saved and reloaded independently
/// while reflecting a coherent multi-aggregate business scenario.
/// </summary>
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class MultiAggregateIntegrationTests(SqlServerEventStoreFixture fixture)
{
	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Cross-aggregate workflow integration test intentionally exercises the full aggregate graph."
	)]
	public async Task SaveAsync_GivenOrderFulfilmentWorkflow_AllAggregatesRestoreWithCoherentState(
		CancellationToken cancellationToken
	)
	{
		// Arrange: set up independent stores (each with their own isolated table)
		var customerStore = fixture.CreateEventStore<CustomerAggregate>();
		var inventoryStore = fixture.CreateEventStore<InventoryAggregate>();
		var orderStore = fixture.CreateEventStore<OrderAggregate>();

		// --- Phase 1: Register customer ---
		CustomerAggregate customer = new();
		customer.Details.Id = $"{Guid.NewGuid()}";
		customer.RegisterCustomer("Alice Johnson", "alice@example.com");
		await customerStore.SaveAsync(customer, cancellationToken);

		// --- Phase 2: Initialize inventory ---
		InventoryAggregate inventory = new();
		inventory.Details.Id = $"{Guid.NewGuid()}";
		inventory.Create("widget-1", "Premium Widget", "loc-1", "Main Warehouse", initialQuantity: 100);
		await inventoryStore.SaveAsync(inventory, cancellationToken);

		// --- Phase 3: Create order for the customer ---
		OrderAggregate order = new();
		order.Details.Id = $"{Guid.NewGuid()}";
		order
			.CreateOrder(customer.Id())
			.AddLineItem("widget-1", "Premium Widget", 5, 19.99m)
			.SetShippingAddress("789 Commerce Blvd");

		await orderStore.SaveAsync(order, cancellationToken);

		// --- Phase 4: Reserve inventory ---
		inventory.ReserveStock(5, order.Id());
		await inventoryStore.SaveAsync(inventory, cancellationToken);

		// --- Phase 5: Confirm, ship, complete ---
		order.ConfirmOrder();
		order.ShipOrder();
		await orderStore.SaveAsync(order, cancellationToken);

		inventory.ShipStock(5, order.Id());
		await inventoryStore.SaveAsync(inventory, cancellationToken);

		order.CompleteOrder();
		await orderStore.SaveAsync(order, cancellationToken);

		// Act: Reload all three aggregates from their respective stores
		var loadedCustomerResult = await customerStore.GetAsync(customer.Id(), cancellationToken);
		var loadedCustomer = loadedCustomerResult.IsSuccess ? loadedCustomerResult.Value : null;
		var loadedInventoryResult = await inventoryStore.GetAsync(inventory.Id(), cancellationToken);
		var loadedInventory = loadedInventoryResult.IsSuccess ? loadedInventoryResult.Value : null;
		var loadedOrderResult = await orderStore.GetAsync(order.Id(), cancellationToken);
		var loadedOrder = loadedOrderResult.IsSuccess ? loadedOrderResult.Value : null;

		// Assert: Customer
		await Assert.That(loadedCustomer).IsNotNull();
		await Assert.That(loadedCustomer!.Name).IsEqualTo("Alice Johnson");
		await Assert.That(loadedCustomer.Email).IsEqualTo("alice@example.com");
		await Assert.That(loadedCustomer.IsActive).IsTrue();

		// Assert: Order
		await Assert.That(loadedOrder).IsNotNull();
		await Assert.That(loadedOrder!.CustomerId).IsEqualTo(customer.Id());
		await Assert.That(loadedOrder.Status).IsEqualTo(OrderStatusCode.Completed);
		await Assert.That(loadedOrder.TotalAmount).IsEqualTo(99.95m);
		await Assert.That(loadedOrder.LineItems).Count().IsEqualTo(1);
		await Assert.That(loadedOrder.CompletedAt).IsNotNull();

		// Assert: Inventory
		await Assert.That(loadedInventory).IsNotNull();
		await Assert.That(loadedInventory!.QuantityOnHand).IsEqualTo(95);
		await Assert.That(loadedInventory.ReservedQuantity).IsEqualTo(0);
		await Assert.That(loadedInventory.AvailableQuantity).IsEqualTo(95);
	}

	[Test]
	public async Task SaveAsync_GivenOrderCancellationWorkflow_InventoryReservationIsReleased(
		CancellationToken cancellationToken
	)
	{
		var inventoryStore = fixture.CreateEventStore<InventoryAggregate>();
		var orderStore = fixture.CreateEventStore<OrderAggregate>();

		// --- Set up inventory ---
		InventoryAggregate inventory = new();
		inventory.Details.Id = $"{Guid.NewGuid()}";
		inventory.Create("gadget-1", "Super Gadget", "loc-2", "Returns Warehouse", initialQuantity: 50);
		await inventoryStore.SaveAsync(inventory, cancellationToken);

		// --- Create and confirm order ---
		OrderAggregate order = new();
		order.Details.Id = $"{Guid.NewGuid()}";
		order.CreateOrder("customer-cancel").AddLineItem("gadget-1", "Super Gadget", 10, 15.00m);
		inventory.ReserveStock(10, order.Id());
		order.ConfirmOrder();

		await orderStore.SaveAsync(order, cancellationToken);
		await inventoryStore.SaveAsync(inventory, cancellationToken);

		// --- Cancel order and release inventory ---
		order.CancelOrder();
		inventory.ReleaseReservation(10, order.Id());

		await orderStore.SaveAsync(order, cancellationToken);
		await inventoryStore.SaveAsync(inventory, cancellationToken);

		// Act: Reload
		var loadedOrderResult = await orderStore.GetAsync(order.Id(), cancellationToken);
		var loadedOrder = loadedOrderResult.IsSuccess ? loadedOrderResult.Value : null;
		var loadedInventoryResult = await inventoryStore.GetAsync(inventory.Id(), cancellationToken);
		var loadedInventory = loadedInventoryResult.IsSuccess ? loadedInventoryResult.Value : null;

		// Assert
		await Assert.That(loadedOrder).IsNotNull();
		await Assert.That(loadedOrder!.Status).IsEqualTo(OrderStatus.Cancelled);

		await Assert.That(loadedInventory).IsNotNull();
		await Assert.That(loadedInventory!.QuantityOnHand).IsEqualTo(50);
		await Assert.That(loadedInventory.ReservedQuantity).IsEqualTo(0);
		await Assert.That(loadedInventory.AvailableQuantity).IsEqualTo(50);
	}

	[Test]
	public async Task SaveAsync_GivenCustomerDeactivatedAfterOrderPlaced_OrderRemainsIntact(
		CancellationToken cancellationToken
	)
	{
		var customerStore = fixture.CreateEventStore<CustomerAggregate>();
		var orderStore = fixture.CreateEventStore<OrderAggregate>();

		CustomerAggregate customer = new();
		customer.Details.Id = $"{Guid.NewGuid()}";
		customer.RegisterCustomer("Bob Smith", "bob@example.com");
		await customerStore.SaveAsync(customer, cancellationToken);

		OrderAggregate order = new();
		order.Details.Id = $"{Guid.NewGuid()}";
		order.CreateOrder(customer.Id());
		order.AddLineItem("prod-1", "Item", 1, 25m);
		order.SetShippingAddress("123 Lane");
		order.ConfirmOrder();
		await orderStore.SaveAsync(order, cancellationToken);

		// Deactivate customer after the order is confirmed
		customer.Deactivate();
		await customerStore.SaveAsync(customer, cancellationToken);

		// Reload both
		var loadedCustomerResult = await customerStore.GetAsync(customer.Id(), cancellationToken);
		var loadedCustomer = loadedCustomerResult.IsSuccess ? loadedCustomerResult.Value : null;
		var loadedOrderResult = await orderStore.GetAsync(order.Id(), cancellationToken);
		var loadedOrder = loadedOrderResult.IsSuccess ? loadedOrderResult.Value : null;

		await Assert.That(loadedCustomer).IsNotNull();
		await Assert.That(loadedCustomer!.IsActive).IsFalse();

		// The order is unaffected by the customer deactivation
		await Assert.That(loadedOrder).IsNotNull();
		await Assert.That(loadedOrder!.Status).IsEqualTo(OrderStatus.Confirmed);
		await Assert.That(loadedOrder.CustomerId).IsEqualTo(customer.Id());
	}
}
