using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Fixtures;
using Purview.EventSourcing.Samples.ValueObjects;

namespace Purview.EventSourcing.Samples.Services;

[NotInParallel("SamplesAppHost")]
[ClassDataSource<AppHostFixture>(Shared = SharedType.PerTestSession)]
public sealed class CartCheckoutServiceTests(AppHostFixture fixture)
{
	[Test]
	public async Task CheckoutAsync_GivenAvailableStock_CreatesOrderAndReservesInventoryAtomically(
		CancellationToken cancellationToken
	)
	{
		var store = fixture.QueryableEventStore();

		var checkoutService = fixture.GetRequiredService<ICartCheckoutService>();
		var customerStore = fixture.GetRequiredService<IQueryableEventStore>();
		var inventoryStore = fixture.GetRequiredService<IQueryableEventStore>();
		var orderStore = fixture.GetRequiredService<IQueryableEventStore>();

		var customer = await CreateCustomerAsync(customerStore, cancellationToken);
		var inventory = await CreateInventoryAsync(inventoryStore, quantityOnHand: 10, cancellationToken);

		var result = await checkoutService.CheckoutAsync(
			customer.Id(),
			[new(inventory.ProductId, inventory.ProductName, inventory.Id(), 3, 19.99m)],
			"1 Event Sourcing Way",
			cancellationToken
		);

		await Assert.That(result.Succeeded).IsTrue();
		await Assert.That(result.Order).IsNotNull();

		var savedOrderResult = await orderStore.GetAsync<OrderAggregate>(result.Order!.Id(), cancellationToken);
		var savedOrder = savedOrderResult.IsSuccess ? savedOrderResult.Value : null;
		var savedInventoryResult = await inventoryStore.GetAsync<InventoryAggregate>(inventory.Id(), cancellationToken);
		var savedInventory = savedInventoryResult.IsSuccess ? savedInventoryResult.Value : null;
		var orderCount = await orderStore.CountAsync<OrderAggregate>(
			order => order.CustomerId == customer.Id(),
			cancellationToken
		);

		await Assert.That(orderCount).IsEqualTo(1L);
		await Assert.That(savedOrder).IsNotNull();
		await Assert.That(savedOrder!.Status).IsEqualTo(OrderStatusCode.Confirmed);
		await Assert.That(savedOrder.TotalAmount).IsEqualTo(result.Order.TotalAmount);
		await Assert.That(savedInventory).IsNotNull();
		await Assert.That(savedInventory!.ReservedQuantity).IsEqualTo(3);
		await Assert.That(savedInventory.AvailableQuantity).IsEqualTo(7);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Checkout atomicity scenario intentionally exercises the full aggregate/service graph."
	)]
	public async Task CheckoutAsync_WhenInventoryChangesBeforeCommit_RollsBackOrderAndPreservesAtomicity(
		CancellationToken cancellationToken
	)
	{
		var inventoryId = string.Empty;

		var serviceProvider = fixture.CloneServices(services =>
		{
			var descriptor = services.Last(service => service.ServiceType == typeof(IEventStoreTransactionFactory));
			services.Remove(descriptor);
			services.AddSingleton<IEventStoreTransactionFactory>(serviceProvider => new HookedTransactionFactory(
				CreateService<IEventStoreTransactionFactory>(serviceProvider, descriptor),
				async hookCancellationToken =>
				{
					await using var hookScope = serviceProvider.CreateAsyncScope();
					var inventoryStore = hookScope.ServiceProvider.GetRequiredService<IQueryableEventStore>();
					var inventoryResult = await inventoryStore.GetAsync<InventoryAggregate>(
						inventoryId,
						hookCancellationToken
					);
					var inventory = inventoryResult.IsSuccess ? inventoryResult.Value : null;

					inventory!.ReserveStock(3, "concurrent-order");
					await inventoryStore.SaveAsync(inventory, hookCancellationToken);
				}
			));
		});

		await using var scope = serviceProvider.CreateAsyncScope();

		var checkoutService = scope.ServiceProvider.GetRequiredService<ICartCheckoutService>();
		var customerStore = scope.ServiceProvider.GetRequiredService<IQueryableEventStore>();
		var inventoryStore = scope.ServiceProvider.GetRequiredService<IQueryableEventStore>();
		var orderStore = scope.ServiceProvider.GetRequiredService<IQueryableEventStore>();

		var customer = await CreateCustomerAsync(customerStore, cancellationToken);
		var inventory = await CreateInventoryAsync(inventoryStore, quantityOnHand: 10, cancellationToken);
		inventoryId = inventory.Id();

		var result = await checkoutService.CheckoutAsync(
			customer.Id(),
			[new CartItem(inventory.ProductId, inventory.ProductName, inventory.Id(), 8, 19.99m)],
			"2 Event Sourcing Way",
			cancellationToken
		);

		var orderCount = await orderStore.CountAsync<OrderAggregate>(
			order => order.CustomerId == customer.Id(),
			cancellationToken
		);
		var savedInventoryResult = await inventoryStore.GetAsync<InventoryAggregate>(inventory.Id(), cancellationToken);
		var savedInventory = savedInventoryResult.IsSuccess ? savedInventoryResult.Value : null;

		await Assert.That(result.Succeeded).IsFalse();
		await Assert.That(result.ErrorMessage).Contains("Nothing was saved");
		await Assert.That(orderCount).IsEqualTo(0L);
		await Assert.That(savedInventory).IsNotNull();
		await Assert.That(savedInventory!.ReservedQuantity).IsEqualTo(3);
		await Assert.That(savedInventory.AvailableQuantity).IsEqualTo(7);
	}

	static T CreateService<T>(IServiceProvider serviceProvider, ServiceDescriptor descriptor)
		where T : class
	{
		return descriptor.ImplementationInstance is T instance ? instance
			: descriptor.ImplementationFactory is null
				? (T)ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType!)
			: (T)descriptor.ImplementationFactory(serviceProvider);
	}

	static async Task<CustomerAggregate> CreateCustomerAsync(
		IQueryableEventStore customerStore,
		CancellationToken cancellationToken
	)
	{
		var customer = await customerStore.CreateAsync<CustomerAggregate>(cancellationToken: cancellationToken);
		customer.RegisterCustomer($"Cart Test {Guid.NewGuid():N}", $"{Guid.NewGuid():N}@example.com");

		var result = await customerStore.SaveAsync(customer, cancellationToken);
		return result.Value.Aggregate;
	}

	static async Task<InventoryAggregate> CreateInventoryAsync(
		IQueryableEventStore inventoryStore,
		int quantityOnHand,
		CancellationToken cancellationToken
	)
	{
		var inventory = await inventoryStore.CreateAsync<InventoryAggregate>(cancellationToken: cancellationToken);
		inventory.Create(
			$"sku-{Guid.NewGuid():N}",
			"Transactional Widget",
			$"loc-{Guid.NewGuid():N}",
			"Main Warehouse",
			quantityOnHand
		);

		var result = await inventoryStore.SaveAsync(inventory, cancellationToken);
		return result.Value.Aggregate;
	}

	sealed class HookedTransactionFactory(
		IEventStoreTransactionFactory innerFactory,
		Func<CancellationToken, Task> beforeCommit
	) : IEventStoreTransactionFactory
	{
		public IEventStoreTransaction Create(string? correlationId = null) =>
			new HookedTransaction(innerFactory.Create(correlationId), beforeCommit);
	}

	sealed class HookedTransaction(IEventStoreTransaction innerTransaction, Func<CancellationToken, Task> beforeCommit)
		: IEventStoreTransaction
	{
		int _beforeCommitInvoked;

		public string CorrelationId => innerTransaction.CorrelationId;

		public void Enlist<T>(T aggregate, IEventStore eventStore, EventStoreOperationContext? operationContext = null)
			where T : class, IAggregate, new() => innerTransaction.Enlist(aggregate, eventStore, operationContext);

		public void Enlist<T>(
			T aggregate,
			IEventStoreCore<T> eventStore,
			EventStoreOperationContext? operationContext = null
		)
			where T : class, IAggregate, new() => innerTransaction.Enlist(aggregate, eventStore, operationContext);

		public async Task<TransactionResult> CommitAsync(CancellationToken cancellationToken = default)
		{
			if (Interlocked.Exchange(ref _beforeCommitInvoked, 1) == 0)
				await beforeCommit(cancellationToken);

			return await innerTransaction.CommitAsync(cancellationToken);
		}

		public ValueTask DisposeAsync() => innerTransaction.DisposeAsync();
	}
}
