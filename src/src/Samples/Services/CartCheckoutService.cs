using System.Collections.Concurrent;
using Purview.EventSourcing.Samples.Domain;

namespace Purview.EventSourcing.Samples.Services;

public sealed class CartCheckoutService(IEventStoreTransactionFactory transactionFactory, IQueryableEventStore store)
	: ICartCheckoutService
{
	static readonly ConcurrentDictionary<string, decimal> UnitPrices = new(StringComparer.OrdinalIgnoreCase);

	public async Task<CartCheckoutResult> CheckoutAsync(
		string customerId,
		IReadOnlyList<CartItem> items,
		string? shippingAddress,
		CancellationToken cancellationToken = default
	)
	{
		if (items == null || items.Count == 0)
			return CartCheckoutResult.Fail("Cart is empty.");

		var customerResult = await store.GetAsync<CustomerAggregate>(customerId, cancellationToken);
		if (customerResult.IsFailure)
			return CartCheckoutResult.Fail("Customer not found.");

		var customer = customerResult.Value;
		if (!customer.IsActive)
			return CartCheckoutResult.Fail($"Customer '{customer.Name}' is not active.");

		Dictionary<string, InventoryReservation> inventoryReservations = new(StringComparer.OrdinalIgnoreCase);
		foreach (var item in items)
		{
			if (!inventoryReservations.TryGetValue(item.InventoryId, out var reservation))
			{
				var inventoryResult = await store.GetAsync<InventoryAggregate>(item.InventoryId, cancellationToken);
				if (inventoryResult.IsFailure)
					return CartCheckoutResult.Fail($"Product '{item.ProductName}' is no longer available.");

				reservation = new InventoryReservation(inventoryResult.Value);
				inventoryReservations.Add(item.InventoryId, reservation);
			}

			reservation.Add(item.Quantity);
		}

		foreach (var reservation in inventoryReservations.Values)
		{
			if (reservation.Aggregate.AvailableQuantity < reservation.Quantity)
			{
				return CartCheckoutResult.Fail(
					$"Insufficient stock for '{reservation.Aggregate.ProductName}'. Available: {reservation.Aggregate.AvailableQuantity}, requested: {reservation.Quantity}."
				);
			}
		}

		var order = await store.CreateAsync<OrderAggregate>(cancellationToken: cancellationToken);
		order.CreateOrder(customerId);

		foreach (var item in items)
		{
			var inventory = inventoryReservations[item.InventoryId].Aggregate;
			var unitPrice = GetUnitPrice(inventory.ProductId);
			order.AddLineItem(inventory.ProductId, inventory.ProductName, item.Quantity, unitPrice);
		}

		if (!string.IsNullOrWhiteSpace(shippingAddress))
			order.SetShippingAddress(shippingAddress);

		order.ConfirmOrder();

		foreach (var reservation in inventoryReservations.Values)
			reservation.Aggregate.ReserveStock(reservation.Quantity, order.Id());

		await using var transaction = transactionFactory.Create();
		transaction.Enlist(order, store);

		foreach (var reservation in inventoryReservations.Values)
			transaction.Enlist(reservation.Aggregate, store);

		var transactionResult = await transaction.CommitAsync(cancellationToken);
		return transactionResult.Success
			? CartCheckoutResult.Success(order)
			: CartCheckoutResult.Fail(
				"Unable to complete checkout. Nothing was saved. Please review your cart and try again."
			);
	}

	static decimal GetUnitPrice(string productId)
	{
		if (UnitPrices.TryGetValue(productId, out var cached))
			return cached;

		var hash = Math.Abs(productId.GetHashCode(StringComparison.Ordinal));
		var price = Math.Round(9.99m + (hash % 9000 / 100m), 2);
		return UnitPrices.GetOrAdd(productId, price);
	}

	sealed class InventoryReservation(InventoryAggregate aggregate)
	{
		public InventoryAggregate Aggregate { get; } = aggregate;
		public int Quantity { get; private set; }

		public void Add(int quantity) => Quantity += quantity;
	}
}
