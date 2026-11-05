using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.QuickStart.Infrastructure;
using Purview.Results;

EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, _) => cts.Cancel();

ServiceCollection services = new();
services.AddEventSourcing();
services.AddSingleton<InMemoryFailurePlan>();
services.AddSingleton(typeof(IQueryableEventStoreCore<>), typeof(InMemoryTransactionalEventStore<>));
services.AddTransient<IQueryableEventStore, QueryableEventStoreFacade>();

using var serviceProvider = services.BuildServiceProvider();
using var scope = serviceProvider.CreateScope();

var store = scope.ServiceProvider.GetRequiredService<IQueryableEventStore>();
var transactionFactory = scope.ServiceProvider.GetRequiredService<IEventStoreTransactionFactory>();
var failurePlan = scope.ServiceProvider.GetRequiredService<InMemoryFailurePlan>();

Console.WriteLine("Purview EventSourcing quick start");
Console.WriteLine("Related aggregates committed together, then rolled back on failure.");
Console.WriteLine();

await SeedAsync(store);
await RunSuccessfulCheckoutAsync(store, transactionFactory);
await RunRollbackDemoAsync(store, transactionFactory, failurePlan);

await TestValidatorsAsync(cts.Token);

static async Task TestValidatorsAsync(CancellationToken cancellationToken)
{
	await using ValidatorScenarios scenarios = new();

	await scenarios.ValidateAsync(cancellationToken);
}

static async Task SeedAsync(IQueryableEventStore store)
{
	var customer = await store.CreateAsync<CustomerAggregate>("customer-quickstart");
	customer.RegisterCustomer("Alice Johnson", "alice@example.com");
	await store.SaveAsync(customer);

	var keyboard = await store.CreateAsync<InventoryAggregate>("inventory-keyboard");
	keyboard.Create("SKU-KEYBOARD", "Wireless Keyboard", "LOC-1", "Main Warehouse", initialQuantity: 25);
	await store.SaveAsync(keyboard);

	var dock = await store.CreateAsync<InventoryAggregate>("inventory-dock");
	dock.Create("SKU-DOCK", "USB-C Dock", "LOC-1", "Main Warehouse", initialQuantity: 15);
	await store.SaveAsync(dock);

	Console.WriteLine("Seeded one customer and two inventory aggregates.");
	await PrintInventorySnapshotAsync(store, "Initial committed stock");
}

static async Task RunSuccessfulCheckoutAsync(
	IQueryableEventStore store,
	IEventStoreTransactionFactory transactionFactory
)
{
	// The seeded aggregates must be there; a failure here is a broken sample, not an expected outcome.
	var customer = (await store.GetAsync<CustomerAggregate>("customer-quickstart")).Value;
	var keyboard = (await store.GetAsync<InventoryAggregate>("inventory-keyboard")).Value;
	var dock = (await store.GetAsync<InventoryAggregate>("inventory-dock")).Value;

	var order = await store.CreateAsync<OrderAggregate>("order-success");
	order
		.CreateOrder(customer.Id())
		.AddLineItem(keyboard.ProductId, keyboard.ProductName, quantity: 1, unitPrice: 49.99m)
		.AddLineItem(dock.ProductId, dock.ProductName, quantity: 1, unitPrice: 89.99m)
		.SetShippingAddress("123 Example Street")
		.ConfirmOrder();

	keyboard.ReserveStock(1, order.Id());
	dock.ReserveStock(1, order.Id());

	await using var transaction = transactionFactory.Create("quickstart-success");
	transaction.Enlist(order, store);
	transaction.Enlist(keyboard, store);
	transaction.Enlist(dock, store);

	var result = await transaction.CommitAsync();

	Console.WriteLine("Successful multi-aggregate commit");
	Console.WriteLine($"Correlation: {transaction.CorrelationId}");
	PrintTransactionResult(result);
	await PrintOrderSnapshotAsync(store, "Committed order", "order-success");
	await PrintInventorySnapshotAsync(store, "Committed stock after success");
}

static async Task RunRollbackDemoAsync(
	IQueryableEventStore store,
	IEventStoreTransactionFactory transactionFactory,
	InMemoryFailurePlan failurePlan
)
{
	// The seeded aggregates must be there; a failure here is a broken sample, not an expected outcome.
	var customer = (await store.GetAsync<CustomerAggregate>("customer-quickstart")).Value;
	var keyboard = (await store.GetAsync<InventoryAggregate>("inventory-keyboard")).Value;
	var dock = (await store.GetAsync<InventoryAggregate>("inventory-dock")).Value;

	var order = await store.CreateAsync<OrderAggregate>("order-rollback");
	order.CreateOrder(customer.Id());
	order.AddLineItem(keyboard.ProductId, keyboard.ProductName, quantity: 1, unitPrice: 49.99m);
	order.AddLineItem(dock.ProductId, dock.ProductName, quantity: 1, unitPrice: 89.99m);
	order.SetShippingAddress("456 Rollback Avenue");
	order.ConfirmOrder();

	keyboard.ReserveStock(1, order.Id());
	dock.ReserveStock(1, order.Id());

	failurePlan.FailNextSave<InventoryAggregate>(dock.Id());

	await using var transaction = transactionFactory.Create("quickstart-rollback");
	transaction.Enlist(order, store);
	transaction.Enlist(keyboard, store);
	transaction.Enlist(dock, store);

	var result = await transaction.CommitAsync();
	var persistedOrder = await store.GetAsync<OrderAggregate>("order-rollback");
	var orderCount = await store.CountAsync<OrderAggregate>(null);

	Console.WriteLine("Rollback when one enlisted aggregate fails");
	Console.WriteLine($"Correlation: {transaction.CorrelationId}");
	PrintTransactionResult(result);
	Console.WriteLine($"Rolled-back order persisted: {persistedOrder.IsSuccess}");
	Console.WriteLine($"Committed order count: {orderCount}");
	await PrintInventorySnapshotAsync(store, "Committed stock after rollback");
}

static async Task PrintOrderSnapshotAsync(IQueryableEventStore store, string title, string orderId)
{
	var result = await store.GetAsync<OrderAggregate>(orderId);

	Console.WriteLine(title);
	if (result.IsFailure)
	{
		Console.WriteLine("  (not found)");
		Console.WriteLine();
		return;
	}

	var order = result.Value;

	Console.WriteLine($"  {order.Id()} status={order.Status} total={order.TotalAmount:C}");
	Console.WriteLine();
}

static async Task PrintInventorySnapshotAsync(IQueryableEventStore store, string title)
{
	var keyboard = await store.GetAsync<InventoryAggregate>("inventory-keyboard");
	var dock = await store.GetAsync<InventoryAggregate>("inventory-dock");

	Console.WriteLine(title);
	Console.WriteLine($"  inventory-keyboard {Describe(keyboard)}");
	Console.WriteLine($"  inventory-dock     {Describe(dock)}");
	Console.WriteLine();

	static string Describe(Result<InventoryAggregate, AggregateReadFailure> result) =>
		result.IsFailure
			? "(not found)"
			: $"available={result.Value.AvailableQuantity} reserved={result.Value.ReservedQuantity}";
}

static void PrintTransactionResult(TransactionResult result)
{
	Console.WriteLine($"  Success: {result.Success}");

	foreach (var aggregateResult in result.Results)
	{
		var aggregate = aggregateResult.Aggregate;
		var outcome =
			aggregateResult.Saved ? "saved"
			: aggregateResult.Skipped ? "skipped"
			: "rolled back";
		Console.WriteLine($"  - {aggregate.GetType().Name} '{aggregate.Id()}': {outcome}");

		if (aggregateResult.Error is not null)
			Console.WriteLine($"    Error: {aggregateResult.Error.Message}");
	}

	Console.WriteLine();
}
