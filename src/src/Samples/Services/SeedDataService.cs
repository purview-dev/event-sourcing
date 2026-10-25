using System.Security.Cryptography;
using Purview.EventSourcing.Samples.Domain;

namespace Purview.EventSourcing.Samples.Services;

public sealed class SeedDataService(IQueryableEventStore store) : ISeedDataService
{
	static readonly string[] FirstNames =
	[
		"Alice",
		"Bob",
		"Carol",
		"David",
		"Emma",
		"Frank",
		"Grace",
		"Henry",
		"Iris",
		"James",
		"Karen",
		"Liam",
		"Mia",
		"Noah",
		"Olivia",
		"Peter",
		"Quinn",
		"Rachel",
		"Sam",
		"Tara",
		"Uma",
		"Victor",
		"Wendy",
		"Xavier",
		"Yara",
		"Zach",
		"Abby",
		"Ben",
		"Clara",
		"Dan",
	];
	static readonly string[] Value =
	[
		"Smith",
		"Johnson",
		"Williams",
		"Brown",
		"Jones",
		"Garcia",
		"Miller",
		"Davis",
		"Wilson",
		"Moore",
		"Taylor",
		"Anderson",
		"Thomas",
		"Jackson",
		"White",
		"Harris",
		"Martin",
		"Thompson",
		"Young",
		"Lewis",
	];
	static readonly string[] LastNames = Value;

	static readonly (string ProductId, string ProductName, int InitialQty, int ReorderQty)[] Products =
	[
		("SKU-001", "Wireless Keyboard", 150, 25),
		("SKU-002", "USB-C Hub 7-Port", 200, 30),
		("SKU-003", "27\" 4K Monitor", 45, 10),
		("SKU-004", "Mechanical Keyboard", 80, 15),
		("SKU-005", "Ergonomic Mouse", 175, 40),
		("SKU-006", "Laptop Stand Aluminium", 120, 20),
		("SKU-007", "Webcam 1080p HD", 95, 20),
		("SKU-008", "Noise Cancelling Headset", 60, 10),
		("SKU-009", "External SSD 1TB", 70, 15),
		("SKU-010", "HDMI 2.1 Cable 2m", 300, 50),
		("SKU-011", "Desk Pad XL", 110, 25),
		("SKU-012", "USB-C Power Bank 20k", 85, 15),
		("SKU-013", "Cable Management Kit", 250, 40),
		("SKU-014", "Monitor Arm Single", 55, 10),
		("SKU-015", "Smart LED Desk Lamp", 90, 20),
	];

	static readonly (string LocationId, string LocationName)[] Locations =
	[
		("LOC-001", "Warehouse North"),
		("LOC-002", "Warehouse South"),
		("LOC-003", "Distribution Centre East"),
		("LOC-004", "Fulfilment Hub West"),
	];

	static readonly decimal[] UnitPrices =
	[
		49.99m,
		39.99m,
		399.99m,
		129.99m,
		29.99m,
		59.99m,
		79.99m,
		149.99m,
		89.99m,
		12.99m,
		24.99m,
		49.99m,
		19.99m,
		69.99m,
		34.99m,
	];

	static readonly string[] Addresses =
	[
		"1 King Street, London, EC1A 1BB",
		"42 Queen's Road, Manchester, M1 2AB",
		"15 High Street, Birmingham, B1 1BB",
		"7 Park Lane, Leeds, LS1 1BA",
		"99 Victoria Road, Glasgow, G1 1AA",
		"23 Castle Street, Edinburgh, EH1 1AA",
		"56 Bridge Road, Bristol, BS1 1AA",
		"8 Market Square, Cambridge, CB1 1AA",
		"33 Station Road, Oxford, OX1 1AA",
		"77 Church Lane, Liverpool, L1 1AA",
	];

	public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
	{
		if (!await EnsureLocationsAsync(cancellationToken))
			return false;

		var customerCount = await store.CountAsync<CustomerAggregate>(null, cancellationToken);
		if (customerCount > 0)
			return true;

		var customerIds = await SeedCustomersAsync(cancellationToken);
		if (customerIds is null)
			return false;

		var inventoryIds = await SeedInventoryAsync(cancellationToken);
		return inventoryIds is not null && await SeedOrdersAsync(customerIds, inventoryIds, cancellationToken);
	}

	async Task<bool> EnsureLocationsAsync(CancellationToken cancellationToken)
	{
		HashSet<string> existingLocationIds = new(StringComparer.OrdinalIgnoreCase);
		await foreach (
			var location in store.GetListEnumerableAsync<LocationAggregate>(cancellationToken: cancellationToken)
		)
		{
			existingLocationIds.Add(location.LocationId);
		}

		Dictionary<string, string> knownLocations = new(StringComparer.OrdinalIgnoreCase);
		foreach (var (locationId, locationName) in Locations)
			knownLocations[locationId] = locationName;

		await foreach (
			var inventory in store.GetListEnumerableAsync<InventoryAggregate>(cancellationToken: cancellationToken)
		)
		{
			if (!knownLocations.ContainsKey(inventory.LocationId))
				knownLocations[inventory.LocationId] = inventory.LocationName;
		}

		foreach (var (locationId, locationName) in knownLocations)
		{
			if (existingLocationIds.Contains(locationId))
				continue;

			var location = await store.CreateAsync<LocationAggregate>(locationId, cancellationToken);
			location.Create(locationId, locationName);

			// Another instance seeding the same demo store wins the race; the caller retries.
			if ((await store.SaveAsync(location, cancellationToken)).IsFailure)
				return false;
		}

		return true;
	}

	async Task<string[]?> SeedCustomersAsync(CancellationToken cancellationToken)
	{
		List<string> ids = [];
		var phones = new[] { "+44 7700 900000", "+44 7700 900001", null, "+44 7700 900002", null };

		for (var i = 0; i < FirstNames.Length; i++)
		{
			var first = FirstNames[i];
			var last = LastNames[i % LastNames.Length];
			var email = $"{first}.{last}@example.com";
			var phone = phones[i % phones.Length];

			var customer = await store.CreateAsync<CustomerAggregate>(cancellationToken: cancellationToken);
			customer.RegisterCustomer($"{first} {last}", email);
			if (phone is not null)
				customer.ChangePhoneNumber(phone);

			// Deactivate a handful so the demo shows mixed statuses.
			if (i is 5 or 12 or 21)
				customer.Deactivate();

			var result = await store.SaveAsync(customer, cancellationToken);
			if (result.IsFailure)
				return null;

			ids.Add(result.Value.Aggregate.Id());
		}

		return [.. ids];
	}

	async Task<(string AggregateId, int Index)[]?> SeedInventoryAsync(CancellationToken cancellationToken)
	{
		List<(string, int)> ids = [];

		for (var i = 0; i < Products.Length; i++)
		{
			var (productId, productName, initialQty, _) = Products[i];
			var (locationId, locationName) = Locations[i % Locations.Length];

			var item = await store.CreateAsync<InventoryAggregate>(cancellationToken: cancellationToken);
			item.Create(productId, productName, locationId, locationName, initialQty);

			var result = await store.SaveAsync(item, cancellationToken);
			if (result.IsFailure)
				return null;

			ids.Add((result.Value.Aggregate.Id(), i));
		}

		return [.. ids];
	}

	async Task<bool> SeedOrdersAsync(
		string[] customerIds,
		(string AggregateId, int Index)[] inventoryItems,
		CancellationToken cancellationToken
	)
	{
		// 50 orders distributed across customers and products.
		for (var i = 0; i < 50; i++)
		{
			var customerId = customerIds[i % customerIds.Length];
			var (_, invIdx) = inventoryItems[i % inventoryItems.Length];
			var (productId, productName, _, _) = Products[invIdx];
			var unitPrice = UnitPrices[invIdx];
			var qty = RandomNumberGenerator.GetInt32(1, 5);
			var address = Addresses[i % Addresses.Length];

			var order = await store.CreateAsync<OrderAggregate>(cancellationToken: cancellationToken);
			order
				.CreateOrder(customerId)
				.AddLineItem(productId, productName, qty, unitPrice)
				.SetShippingAddress(address);

			// Advance some orders through the lifecycle for variety.
			var stage = i % 5;
			if (stage >= 1)
				order.ConfirmOrder();
			if (stage >= 2)
				order.ShipOrder();
			if (stage >= 3)
				order.CompleteOrder();
			if (stage == 4)
			{
				// Already completed above; create a separate cancelled one.
				var cancelled = await store.CreateAsync<OrderAggregate>(cancellationToken: cancellationToken);
				cancelled
					.CreateOrder(customerId)
					.AddLineItem(productId, productName, 1, unitPrice)
					.SetShippingAddress(address)
					.ConfirmOrder()
					.CancelOrder();

				await store.SaveAsync(cancelled, cancellationToken);
			}

			if ((await store.SaveAsync(order, cancellationToken)).IsFailure)
				return false;
		}

		return true;
	}
}
