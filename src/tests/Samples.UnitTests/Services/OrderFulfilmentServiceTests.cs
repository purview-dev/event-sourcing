using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.ValueObjects;

namespace Purview.EventSourcing.Samples.Services;

public sealed class OrderFulfilmentServiceTests
{
	static CustomerAggregate ActiveCustomer(string id = "cust-1")
	{
		CustomerAggregate c = new();
		c.Details.Id = id;
		c.RegisterCustomer("Alice Johnson", "alice@example.com");
		return c;
	}

	static InventoryAggregate StockedInventory(string id = "inv-1", int quantity = 100)
	{
		InventoryAggregate i = new();
		i.Details.Id = id;
		i.Create("widget-sku", "Widget", "warehouse-1", "Main Warehouse", initialQuantity: quantity);
		return i;
	}

	static OrderAggregate NewOrder(string? id = null)
	{
		OrderAggregate o = new();
		o.Details.Id = id ?? Guid.NewGuid().ToString("N");
		return o;
	}

	static TransactionResult SuccessfulTransaction(params IAggregate[] aggregates) =>
		new([
			.. aggregates.Select(aggregate => new TransactionAggregateResult(
				aggregate,
				saved: true,
				skipped: false,
				error: null
			)),
		]);

	static TransactionResult FailedTransaction(IAggregate aggregate) =>
		new([
			new TransactionAggregateResult(
				aggregate,
				saved: false,
				skipped: false,
				error: new InvalidOperationException("Commit failed")
			),
		]);

	OrderFulfilmentService CreateService(
		IEventStoreTransactionFactory? transactionFactory = null,
		IQueryableEventStore? store = null
	) => new(transactionFactory ?? IEventStoreTransactionFactory.Mock(), store ?? IQueryableEventStore.Mock());

	[Test]
	public async Task PlaceOrderAsync_GivenNullCustomer_ReturnsFail(CancellationToken cancellationToken)
	{
		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<CustomerAggregate>(Is("missing"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(
				AggregateReadFailure.Failure<CustomerAggregate>(
					new AggregateNotFound(nameof(CustomerAggregate), "missing")
				)
			);

		var result = await CreateService(store: store).PlaceOrderAsync("missing", "inv-1", 1, null, cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).IsNotNullOrEmpty();
	}

	[Test]
	public async Task PlaceOrderAsync_GivenInactiveCustomer_ReturnsFail(CancellationToken cancellationToken)
	{
		var customer = ActiveCustomer();
		customer.Deactivate();

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<CustomerAggregate>(Is(customer.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(customer);

		var result = await CreateService(store: store)
			.PlaceOrderAsync(customer.Id(), "inv-1", 1, null, cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains(customer.Name);
	}

	[Test]
	public async Task PlaceOrderAsync_GivenNullInventory_ReturnsFail(CancellationToken cancellationToken)
	{
		var customer = ActiveCustomer();
		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<CustomerAggregate>(Is(customer.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(customer);
		store
			.GetAsync<InventoryAggregate>(Is("missing-inv"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(
				AggregateReadFailure.Failure<InventoryAggregate>(
					new AggregateNotFound(nameof(InventoryAggregate), "missing")
				)
			);

		var result = await CreateService(store: store)
			.PlaceOrderAsync(customer.Id(), "missing-inv", 1, null, cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).IsNotNullOrEmpty();
	}

	[Test]
	public async Task PlaceOrderAsync_GivenInsufficientStock_ReturnsFail(CancellationToken cancellationToken)
	{
		var customer = ActiveCustomer();
		var inventory = StockedInventory(quantity: 5);

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<CustomerAggregate>(Is(customer.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(customer);
		store
			.GetAsync<InventoryAggregate>(Is(inventory.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(inventory);

		var result = await CreateService(store: store)
			.PlaceOrderAsync(customer.Id(), inventory.Id(), quantity: 10, null, cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Insufficient stock");
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506:Avoid excessive class coupling",
		Justification = "Test wiring: the store, its infrastructure and the result model are all named here."
	)]
	public async Task PlaceOrderAsync_GivenValidData_ReturnsSuccess(CancellationToken cancellationToken)
	{
		var customer = ActiveCustomer();
		var inventory = StockedInventory(quantity: 50);
		var order = NewOrder();

		var transactionFactory = IEventStoreTransactionFactory.Mock();
		var transaction = IEventStoreTransaction.Mock();
		transactionFactory.Create(Any<string?>()).Returns(transaction);
		transaction.CommitAsync(cancellationToken).Returns(SuccessfulTransaction(order, inventory));

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<CustomerAggregate>(Is(customer.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(customer);
		store
			.GetAsync<InventoryAggregate>(Is(inventory.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(inventory);
		store.CreateAsync<OrderAggregate>(Any<string?>(), Is(cancellationToken)).Returns(order);

		var result = await CreateService(transactionFactory, store)
			.PlaceOrderAsync(customer.Id(), inventory.Id(), quantity: 3, "123 Main St", cancellationToken);

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Order).IsNotNull();
		await Assert.That(result.Inventory).IsNotNull();
		transaction.Enlist(Is(order), Is((IEventStore)store), Any<EventStoreOperationContext?>()).WasCalled(Times.Once);
		transaction
			.Enlist(Is(inventory), Is((IEventStore)store), Any<EventStoreOperationContext?>())
			.WasCalled(Times.Once);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Order fulfilment workflow test intentionally exercises the complete aggregate/service graph."
	)]
	public async Task PlaceOrderAsync_GivenValidData_OrderHasLineItemAndIsConfirmed(CancellationToken cancellationToken)
	{
		var customer = ActiveCustomer();
		var inventory = StockedInventory(quantity: 20);
		var order = NewOrder();

		var transactionFactory = IEventStoreTransactionFactory.Mock();
		var transaction = IEventStoreTransaction.Mock();
		transactionFactory.Create(Any<string?>()).Returns(transaction);
		transaction.CommitAsync(cancellationToken).Returns(SuccessfulTransaction(order, inventory));

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<CustomerAggregate>(Is(customer.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(customer);
		store
			.GetAsync<InventoryAggregate>(Is(inventory.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(inventory);
		store.CreateAsync<OrderAggregate>(Any<string?>(), Is(cancellationToken)).Returns(order);

		await CreateService(transactionFactory, store)
			.PlaceOrderAsync(customer.Id(), inventory.Id(), quantity: 2, null, cancellationToken);

		await Assert.That(order.Status).IsEqualTo(OrderStatus.Confirmed);
		await Assert.That(order.LineItems).Count().IsEqualTo(1);
		await Assert.That(order.CustomerId).IsEqualTo(customer.Id());
		await Assert.That(order.TotalAmount).IsGreaterThan(0m);
	}

	[Test]
	public async Task PlaceOrderAsync_WhenTransactionCommitFails_ReturnsFailWithoutSuccess(
		CancellationToken cancellationToken
	)
	{
		var customer = ActiveCustomer();
		var inventory = StockedInventory(quantity: 50);
		var order = NewOrder();

		var transactionFactory = IEventStoreTransactionFactory.Mock();
		var transaction = IEventStoreTransaction.Mock();
		transactionFactory.Create(Any<string?>()).Returns(transaction);
		transaction.CommitAsync(cancellationToken).Returns(FailedTransaction(order));

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<CustomerAggregate>(Is(customer.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(customer);
		store
			.GetAsync<InventoryAggregate>(Is(inventory.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(inventory);
		store.CreateAsync<OrderAggregate>(Any<string?>(), Is(cancellationToken)).Returns(order);

		var result = await CreateService(transactionFactory, store)
			.PlaceOrderAsync(customer.Id(), inventory.Id(), quantity: 1, null, cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Nothing was saved");
		transaction.CommitAsync(Is(cancellationToken)).WasCalled(Times.Once);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506:Avoid excessive class coupling",
		Justification = "Test wiring: the store, its infrastructure and the result model are all named here."
	)]
	public async Task PlaceOrderAsync_WhenTransactionCommitFails_DoesNotCancelOrderInMemory(
		CancellationToken cancellationToken
	)
	{
		var customer = ActiveCustomer();
		var inventory = StockedInventory(quantity: 50);
		var order = NewOrder();

		var transactionFactory = IEventStoreTransactionFactory.Mock();
		var transaction = IEventStoreTransaction.Mock();
		transactionFactory.Create(Any<string?>()).Returns(transaction);
		transaction.CommitAsync(cancellationToken).Returns(FailedTransaction(inventory));

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<CustomerAggregate>(Is(customer.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(customer);
		store
			.GetAsync<InventoryAggregate>(Is(inventory.Id()), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(inventory);
		store.CreateAsync<OrderAggregate>(Any<string?>(), Is(cancellationToken)).Returns(order);

		var result = await CreateService(transactionFactory, store)
			.PlaceOrderAsync(customer.Id(), inventory.Id(), quantity: 1, null, cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Nothing was saved");
		await Assert.That(order.Status).IsEqualTo(OrderStatus.Confirmed);
	}
}
