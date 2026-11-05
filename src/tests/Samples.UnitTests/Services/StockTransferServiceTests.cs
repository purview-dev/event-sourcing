using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Samples.Domain;

namespace Purview.EventSourcing.Samples.Services;

public sealed class StockTransferServiceTests
{
	static InventoryAggregate StockedItem(
		string id,
		string locationId,
		string locationName,
		int qty,
		string productId = "SKU-001"
	)
	{
		InventoryAggregate item = new();
		item.Details.Id = id;
		return item.Create(productId, "Widget", locationId, locationName, initialQuantity: qty);
	}

	static LocationAggregate Location(string id, string name)
	{
		LocationAggregate location = new();
		location.Details.Id = id;
		return location.Create(id, name);
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

	StockTransferService CreateService(
		IEventStoreTransactionFactory? transactionFactory = null,
		IQueryableEventStore? store = null
	) => new(transactionFactory ?? IEventStoreTransactionFactory.Mock(), store ?? IQueryableEventStore.Mock());

	[Test]
	public async Task TransferAsync_GivenSameSourceAndDestinationLocation_ReturnsFail(
		CancellationToken cancellationToken
	)
	{
		var source = StockedItem("inv-1", "LOC-001", "Warehouse North", 100);
		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("inv-1"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(source);

		var result = await CreateService(store: store).TransferAsync("inv-1", "LOC-001", 5, "test", cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("different");
	}

	[Test]
	public async Task TransferAsync_GivenNullSource_ReturnsFail(CancellationToken cancellationToken)
	{
		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("missing"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(
				AggregateReadFailure.Failure<InventoryAggregate>(
					new AggregateNotFound(nameof(InventoryAggregate), "missing")
				)
			);

		var result = await CreateService(store: store).TransferAsync("missing", "inv-2", 5, "test", cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Source");
	}

	[Test]
	public async Task TransferAsync_GivenNullDestination_ReturnsFail(CancellationToken cancellationToken)
	{
		var source = StockedItem("inv-1", "LOC-001", "Warehouse North", 100);
		var sourceLocation = Location("LOC-001", "Warehouse North");
		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("inv-1"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(source);
		store
			.GetAsync<LocationAggregate>(Is("LOC-001"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(sourceLocation);
		store
			.GetAsync<LocationAggregate>(Is("missing"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(
				AggregateReadFailure.Failure<LocationAggregate>(
					new AggregateNotFound(nameof(LocationAggregate), "missing")
				)
			);

		var result = await CreateService(store: store).TransferAsync("inv-1", "missing", 5, "test", cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Destination");
	}

	[Test]
	public async Task TransferAsync_GivenMissingSourceLocationAggregate_ReturnsFail(CancellationToken cancellationToken)
	{
		var source = StockedItem("inv-1", "LOC-001", "Warehouse North", 100, productId: "SKU-001");
		var destinationLocation = Location("LOC-002", "Warehouse South");
		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("inv-1"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(source);
		store
			.GetAsync<LocationAggregate>(Is("LOC-001"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(
				AggregateReadFailure.Failure<LocationAggregate>(
					new AggregateNotFound(nameof(LocationAggregate), "missing")
				)
			);
		store
			.GetAsync<LocationAggregate>(Is("LOC-002"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(destinationLocation);

		var result = await CreateService(store: store).TransferAsync("inv-1", "LOC-002", 5, "test", cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Source location");
	}

	[Test]
	public async Task TransferAsync_GivenInsufficientStock_ReturnsFail(CancellationToken cancellationToken)
	{
		var source = StockedItem("inv-1", "LOC-001", "Warehouse North", 3);
		var sourceLocation = Location("LOC-001", "Warehouse North");
		var destinationLocation = Location("LOC-002", "Warehouse South");
		var dest = StockedItem("inv-2", "LOC-002", "Warehouse South", 0);

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("inv-1"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(source);
		store
			.GetAsync<LocationAggregate>(Is("LOC-001"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(sourceLocation);
		store
			.GetAsync<LocationAggregate>(Is("LOC-002"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(destinationLocation);
		store
			.FirstOrDefaultAsync(
				Any<Expression<Func<InventoryAggregate, bool>>>(),
				Any<Func<IQueryable<InventoryAggregate>, IQueryable<InventoryAggregate>>?>(),
				Is(cancellationToken)
			)
			.Returns(dest);

		var result = await CreateService(store: store).TransferAsync("inv-1", "LOC-002", 10, "test", cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Insufficient");
	}

	[Test]
	public async Task TransferAsync_GivenValidData_ReturnsSuccess(CancellationToken cancellationToken)
	{
		var source = StockedItem("inv-1", "LOC-001", "Warehouse North", 100);
		var sourceLocation = Location("LOC-001", "Warehouse North");
		var destinationLocation = Location("LOC-002", "Warehouse South");
		var dest = StockedItem("inv-2", "LOC-002", "Warehouse South", 20);

		var transactionFactory = IEventStoreTransactionFactory.Mock();
		var transaction = IEventStoreTransaction.Mock();
		transactionFactory.Create(Any<string?>()).Returns(transaction);
		transaction.CommitAsync(cancellationToken).Returns(SuccessfulTransaction(source, dest));

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("inv-1"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(source);
		store
			.GetAsync<LocationAggregate>(Is("LOC-001"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(sourceLocation);
		store
			.GetAsync<LocationAggregate>(Is("LOC-002"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(destinationLocation);
		store
			.FirstOrDefaultAsync(
				Any<Expression<Func<InventoryAggregate, bool>>>(),
				Any<Func<IQueryable<InventoryAggregate>, IQueryable<InventoryAggregate>>?>(),
				Is(cancellationToken)
			)
			.Returns(dest);

		var result = await CreateService(transactionFactory, store)
			.TransferAsync("inv-1", "LOC-002", 15, "rebalance", cancellationToken);

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Quantity).IsEqualTo(15);
	}

	[Test]
	public async Task TransferAsync_GivenValidData_AdjustsSourceAndDestinationCorrectly(
		CancellationToken cancellationToken
	)
	{
		var source = StockedItem("inv-1", "LOC-001", "Warehouse North", 100);
		var sourceLocation = Location("LOC-001", "Warehouse North");
		var destinationLocation = Location("LOC-002", "Warehouse South");
		var dest = StockedItem("inv-2", "LOC-002", "Warehouse South", 20);

		var transactionFactory = IEventStoreTransactionFactory.Mock();
		var transaction = IEventStoreTransaction.Mock();
		transactionFactory.Create(Any<string?>()).Returns(transaction);
		transaction.CommitAsync(cancellationToken).Returns(SuccessfulTransaction(source, dest));

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("inv-1"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(source);
		store
			.GetAsync<LocationAggregate>(Is("LOC-001"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(sourceLocation);
		store
			.GetAsync<LocationAggregate>(Is("LOC-002"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(destinationLocation);
		store
			.FirstOrDefaultAsync(
				Any<Expression<Func<InventoryAggregate, bool>>>(),
				Any<Func<IQueryable<InventoryAggregate>, IQueryable<InventoryAggregate>>?>(),
				Is(cancellationToken)
			)
			.Returns(dest);

		await CreateService(transactionFactory, store)
			.TransferAsync("inv-1", "LOC-002", 15, "rebalance", cancellationToken);

		await Assert.That(source.QuantityOnHand).IsEqualTo(85);
		await Assert.That(dest.QuantityOnHand).IsEqualTo(35);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Stock transfer scenario intentionally exercises the full aggregate/service graph."
	)]
	public async Task TransferAsync_WhenDestinationInventoryMissing_CreatesNewInventoryAtDestination(
		CancellationToken cancellationToken
	)
	{
		var source = StockedItem("inv-1", "LOC-001", "Warehouse North", 100);
		var sourceLocation = Location("LOC-001", "Warehouse North");
		var destinationLocation = Location("LOC-002", "Warehouse South");
		InventoryAggregate createdDestination = new();
		createdDestination.Details.Id = "inv-2";

		var transactionFactory = IEventStoreTransactionFactory.Mock();
		var transaction = IEventStoreTransaction.Mock();
		transactionFactory.Create(Any<string?>()).Returns(transaction);
		transaction.CommitAsync(cancellationToken).Returns(SuccessfulTransaction(source, createdDestination));

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("inv-1"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(source);
		store
			.GetAsync<LocationAggregate>(Is("LOC-001"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(sourceLocation);
		store
			.GetAsync<LocationAggregate>(Is("LOC-002"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(destinationLocation);
		store
			.FirstOrDefaultAsync(
				Any<Expression<Func<InventoryAggregate, bool>>>(),
				Any<Func<IQueryable<InventoryAggregate>, IQueryable<InventoryAggregate>>?>(),
				Is(cancellationToken)
			)
			.Returns((InventoryAggregate?)null);
		store.CreateAsync<InventoryAggregate>(Any<string?>(), Is(cancellationToken)).Returns(createdDestination);

		var result = await CreateService(transactionFactory, store)
			.TransferAsync("inv-1", "LOC-002", 10, "test", cancellationToken);

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(createdDestination.LocationId).IsEqualTo("LOC-002");
		await Assert.That(createdDestination.LocationName).IsEqualTo("Warehouse South");
		await Assert.That(createdDestination.ProductId).IsEqualTo(source.ProductId);
		await Assert.That(createdDestination.QuantityOnHand).IsEqualTo(10);
		store.CreateAsync<InventoryAggregate>(Any<string?>(), Is(cancellationToken)).WasCalled(Times.Once);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506:Avoid excessive class coupling",
		Justification = "Test wiring: the store, its infrastructure and the result model are all named here."
	)]
	public async Task TransferAsync_WhenTransactionCommitFails_ReturnsFail(CancellationToken cancellationToken)
	{
		var source = StockedItem("inv-1", "LOC-001", "Warehouse North", 100);
		var sourceLocation = Location("LOC-001", "Warehouse North");
		var destinationLocation = Location("LOC-002", "Warehouse South");
		var dest = StockedItem("inv-2", "LOC-002", "Warehouse South", 0);

		var transactionFactory = IEventStoreTransactionFactory.Mock();
		var transaction = IEventStoreTransaction.Mock();
		transactionFactory.Create(Any<string?>()).Returns(transaction);
		transaction.CommitAsync(cancellationToken).Returns(FailedTransaction(source));

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("inv-1"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(source);
		store
			.GetAsync<LocationAggregate>(Is("LOC-001"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(sourceLocation);
		store
			.GetAsync<LocationAggregate>(Is("LOC-002"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(destinationLocation);
		store
			.FirstOrDefaultAsync(
				Any<Expression<Func<InventoryAggregate, bool>>>(),
				Any<Func<IQueryable<InventoryAggregate>, IQueryable<InventoryAggregate>>?>(),
				Is(cancellationToken)
			)
			.Returns(dest);

		var result = await CreateService(transactionFactory, store)
			.TransferAsync("inv-1", "LOC-002", 10, "test", cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Nothing was saved");
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Compensation path scenario intentionally exercises the full aggregate/service graph."
	)]
	public async Task TransferAsync_WhenTransactionCommitFails_DoesNotApplyCompensation(
		CancellationToken cancellationToken
	)
	{
		var source = StockedItem("inv-1", "LOC-001", "Warehouse North", 100);
		var sourceLocation = Location("LOC-001", "Warehouse North");
		var destinationLocation = Location("LOC-002", "Warehouse South");
		var dest = StockedItem("inv-2", "LOC-002", "Warehouse South", 0);

		var transactionFactory = IEventStoreTransactionFactory.Mock();
		var transaction = IEventStoreTransaction.Mock();
		transactionFactory.Create(Any<string?>()).Returns(transaction);
		transaction.CommitAsync(cancellationToken).Returns(FailedTransaction(dest));

		var store = IQueryableEventStore.Mock();
		store
			.GetAsync<InventoryAggregate>(Is("inv-1"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(source);
		store
			.GetAsync<LocationAggregate>(Is("LOC-001"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(sourceLocation);
		store
			.GetAsync<LocationAggregate>(Is("LOC-002"), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(destinationLocation);
		store
			.FirstOrDefaultAsync(
				Any<Expression<Func<InventoryAggregate, bool>>>(),
				Any<Func<IQueryable<InventoryAggregate>, IQueryable<InventoryAggregate>>?>(),
				Is(cancellationToken)
			)
			.Returns(dest);

		var result = await CreateService(transactionFactory, store)
			.TransferAsync("inv-1", "LOC-002", 10, "test", cancellationToken);

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Nothing was saved");
		await Assert.That(source.QuantityOnHand).IsEqualTo(90);
		await Assert.That(dest.QuantityOnHand).IsEqualTo(10);
	}
}
