using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Samples.Domain;

namespace Purview.EventSourcing.Samples.Services;

public sealed class OrderFulfilmentService(IEventStoreTransactionFactory transactionFactory, IQueryableEventStore store)
	: IOrderFulfilmentService
{
	// Thread-safe price cache shared across all instances.
	static readonly ConcurrentDictionary<string, decimal> UnitPrices = new(StringComparer.OrdinalIgnoreCase);

	public async Task<FulfilmentResult> PlaceOrderAsync(
		string customerId,
		string inventoryId,
		int quantity,
		string? shippingAddress,
		CancellationToken cancellationToken = default
	)
	{
		var customerResult = await store.GetAsync<CustomerAggregate>(customerId, cancellationToken);
		if (customerResult.IsFailure)
			return FulfilmentResult.Fail("Customer not found.");

		var customer = customerResult.Value;
		if (!customer.IsActive)
			return FulfilmentResult.Fail($"Customer '{customer.Name}' is not active.");

		var inventoryResult = await store.GetAsync<InventoryAggregate>(inventoryId, cancellationToken);
		if (inventoryResult.IsFailure)
			return FulfilmentResult.Fail("Inventory item not found.");

		var inventory = inventoryResult.Value;
		if (inventory.AvailableQuantity < quantity)
			return FulfilmentResult.Fail(
				$"Insufficient stock for '{inventory.ProductName}'. Available: {inventory.AvailableQuantity}, requested: {quantity}."
			);

		var unitPrice = GetUnitPrice(inventory.ProductId);

		// Create and confirm the order.
		var order = await store.CreateAsync<OrderAggregate>(cancellationToken: cancellationToken);
		order.CreateOrder(customerId).AddLineItem(inventory.ProductId, inventory.ProductName, quantity, unitPrice);
		if (!string.IsNullOrWhiteSpace(shippingAddress))
			order.SetShippingAddress(shippingAddress);

		order.ConfirmOrder();

		inventory.ReserveStock(quantity, order.Id());

		await using var transaction = transactionFactory.Create();
		transaction.Enlist(order, store);
		transaction.Enlist(inventory, store);

		var transactionResult = await transaction.CommitAsync(cancellationToken);
		if (transactionResult.Success)
			return FulfilmentResult.Success(order, inventory);

		// A lost concurrency check is reported, not thrown, so the caller can tell a retryable race
		// apart from a genuine failure.
		var conflicted = transactionResult.Results.Any(result => result.Failure is ConcurrencyConflict);

		return FulfilmentResult.Fail(
			conflicted
				? "Another user modified the inventory concurrently. Nothing was saved - please try again."
				: "Failed to place order. Nothing was saved."
		);
	}

	static decimal GetUnitPrice(string productId)
	{
		if (UnitPrices.TryGetValue(productId, out var cached))
			return cached;

		// Derive a stable price from the product ID using a deterministic hash for demo purposes.
		var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(productId));
		var hash = (int)(BitConverter.ToUInt32(hashBytes, 0) & 0x7FFFFFFF);
		var price = Math.Round(9.99m + (hash % 9000 / 100m), 2);
		return UnitPrices.GetOrAdd(productId, price);
	}
}
