using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Domain.OrderEvents;
using Purview.EventSourcing.Samples.ValueObjects;
using Purview.EventSourcing.Serialization;
using Purview.EventSourcing.Services;

namespace Purview.EventSourcing.Benchmarks;

/// <summary>
/// Measures the runtime hot paths of the framework with a focus on the code the source generator
/// emits: aggregate construction, generated command methods, event application and replay,
/// value-object operations, serialization round-trips through the generated converters, the
/// event-store collections, and event-name mapping.
/// </summary>
[MemoryDiagnoser]
public class RuntimePerformanceBenchmarks
{
	EmailAddress _emailA;

	EmailAddress _emailB;

	Money _moneyA;

	Money _moneyB;

	string _moneyJson = string.Empty;

	OrderCreatedEvent _orderCreatedEvent = null!;

	EventRecord[] _replayEvents = null!;

	OrderAggregate _snapshotAggregate = null!;

	string _snapshotJson = string.Empty;

	string _orderJson = string.Empty;

	EventStoreList<int> _list = null!;

	EventStoreSet<int> _set = null!;

	IAggregateEventNameMapper _mapper = null!;

	[GlobalSetup]
	public void GlobalSetup()
	{
		_emailA = EmailAddress.Create("user@example.com");
		_emailB = EmailAddress.Create("other@example.com");
		_moneyA = Money.Create(9.99m, CurrencyCode.Create("USD"));
		_moneyB = Money.Create(19.99m, CurrencyCode.Create("EUR"));
		_moneyJson = EventStoreSerializationHelpers.Serialize(_moneyA);

		_orderCreatedEvent = new OrderCreatedEvent
		{
			CustomerId = "customer-1",
			Metadata = new EventMetadata(1, DateTimeOffset.UtcNow, 1, null, null, null, null),
		};
		_orderJson = EventStoreSerializationHelpers.Serialize(_orderCreatedEvent, _orderCreatedEvent.GetType());

		_replayEvents = new EventRecord[100];
		for (var i = 0; i < _replayEvents.Length; i++)
		{
			object @event = (i % 3) switch
			{
				0 => new OrderCreatedEvent { CustomerId = $"customer-{i}" },
				1 => new ShippingAddressSetEvent { ShippingAddress = $"address-{i}" },
				_ => new NotesUpdatedEvent { Notes = $"note-{i}" },
			};
			_replayEvents[i] = new EventRecord(
				@event,
				new EventMetadata(i + 1, DateTimeOffset.UtcNow, 1, null, null, null, null)
			);
		}

		_snapshotAggregate = CreatePopulatedOrder();
		_snapshotJson = JsonSerializer.Serialize(
			_snapshotAggregate,
			EventStoreSerializationHelpers.JsonSerializerOptions
		);

		_list = new EventStoreList<int>([.. Enumerable.Range(0, 100)]);
		_set = new EventStoreSet<int>([.. Enumerable.Range(0, 100)]);

		_mapper = new ServiceCollection()
			.AddEventSourcing()
			.BuildServiceProvider()
			.GetRequiredService<IAggregateEventNameMapper>();
		// Seed the name maps so GetName runs on the cache-hit path (the hot path measured here).
		_mapper.InitializeAggregate<OrderAggregate>();
	}

	[Benchmark]
	[BenchmarkCategory("Aggregate")]
	public CustomerAggregate AggregateConstruction_Customer() => new();

	[Benchmark]
	[BenchmarkCategory("Aggregate")]
	public OrderAggregate AggregateConstruction_Order() => new();

	[Benchmark]
	[BenchmarkCategory("Command")]
	public OrderAggregate Command_CreateOrder() => new OrderAggregate().CreateOrder("customer-1");

	[Benchmark]
	[BenchmarkCategory("Command")]
	public OrderAggregate Command_ConfirmOrder() => new OrderAggregate().CreateOrder("customer-1").ConfirmOrder();

	[Benchmark]
	[BenchmarkCategory("Command")]
	public OrderAggregate Command_ShipOrder() =>
		new OrderAggregate().CreateOrder("customer-1").ConfirmOrder().ShipOrder();

	[Benchmark]
	[BenchmarkCategory("Command")]
	public OrderAggregate Command_CompleteOrder() =>
		new OrderAggregate().CreateOrder("customer-1").ConfirmOrder().ShipOrder().CompleteOrder();

	[Benchmark]
	[BenchmarkCategory("Command")]
	public OrderAggregate Command_AddLineItem() =>
		new OrderAggregate().AddLineItem(
			new EventStoreList<OrderLineItem>([new OrderLineItem("p1", "Product 1", 2, 9.99m)]),
			19.98m
		);

	[Benchmark]
	[BenchmarkCategory("Command")]
	public CustomerAggregate Command_RegisterCustomer() =>
		new CustomerAggregate().RegisterCustomer("Alice Smith", "alice@example.com", true);

	[Benchmark]
	[BenchmarkCategory("Command")]
	public CustomerAggregate Command_ChangeEmail() => new CustomerAggregate().ChangeEmail("alice@example.com");

	[Benchmark]
	[BenchmarkCategory("Replay")]
	public OrderAggregate Event_ApplyOrderCreated()
	{
		var aggregate = new OrderAggregate();
		((IAggregate)aggregate).ApplyEvent(_orderCreatedEvent, _orderCreatedEvent.Metadata);
		return aggregate;
	}

	[Benchmark]
	[BenchmarkCategory("Replay")]
	public OrderAggregate Replay_100EventStream()
	{
		var aggregate = new OrderAggregate();
		var aggregateEvents = (IAggregate)aggregate;
		foreach (var record in _replayEvents)
			aggregateEvents.ApplyEvent(record.Event, record.Metadata);
		return aggregate;
	}

	[Benchmark]
	[BenchmarkCategory("Replay")]
	public int Event_GetHashCode() => _orderCreatedEvent.GetHashCode();

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public EmailAddress ValueObject_Scalar_Create() => EmailAddress.Create("user@example.com");

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public EmailAddress ValueObject_Scalar_Hydrate() => EmailAddress.Hydrate("user@example.com");

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public EmailAddress ValueObject_Scalar_Implicit() => "user@example.com";

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public bool ValueObject_Scalar_Equality() => _emailA == _emailB;

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public int ValueObject_Scalar_GetHashCode() => _emailA.GetHashCode();

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public int ValueObject_Scalar_CompareTo() => _emailA.CompareTo(_emailB);

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public string ValueObject_Scalar_ToString() => _emailA.ToString();

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public CurrencyCode ValueObject_Scalar_CreateEnum() => CurrencyCode.Create("USD");

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public OrderStatus ValueObject_Enum_Create() => OrderStatus.Create(OrderStatusCode.Confirmed);

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public Money ValueObject_Complex_Create() => Money.Create(9.99m, CurrencyCode.Create("USD"));

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public Money ValueObject_Complex_Hydrate() => Money.Hydrate(9.99m, CurrencyCode.Create("USD"));

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public bool ValueObject_Complex_Equality() => _moneyA == _moneyB;

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public int ValueObject_Complex_GetHashCode() => _moneyA.GetHashCode();

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public int ValueObject_Complex_CompareTo() => _moneyA.CompareTo(_moneyB);

	[Benchmark]
	[BenchmarkCategory("ValueObject")]
	public UserDetails ValueObject_Complex_Record_Create() =>
		UserDetails.Create(Guid.Parse("33333333-3333-3333-3333-333333333333"), "snapshot-user", true);

	[Benchmark]
	[BenchmarkCategory("Serialization")]
	public string Serialization_EventSerialize() =>
		EventStoreSerializationHelpers.Serialize(_orderCreatedEvent, _orderCreatedEvent.GetType());

	[Benchmark]
	[BenchmarkCategory("Serialization")]
	[SuppressMessage(
		"Design",
		"CA2263:Prefer the generic overload",
		Justification = "Measures the reflection-based provider deserialization path."
	)]
	public object? Serialization_EventDeserialize() =>
		EventStoreSerializationHelpers.Deserialize(_orderJson, typeof(OrderCreatedEvent));

	[Benchmark]
	[BenchmarkCategory("Serialization")]
	[SuppressMessage(
		"Design",
		"CA2263:Prefer the generic overload",
		Justification = "Measures the reflection-based provider deserialization path."
	)]
	public object? Serialization_EventRoundTrip()
	{
		var json = EventStoreSerializationHelpers.Serialize(_orderCreatedEvent, _orderCreatedEvent.GetType());
		return EventStoreSerializationHelpers.Deserialize(json, typeof(OrderCreatedEvent));
	}

	[Benchmark]
	[BenchmarkCategory("Serialization")]
	public string Serialization_SnapshotSerialize() =>
		JsonSerializer.Serialize(_snapshotAggregate, EventStoreSerializationHelpers.JsonSerializerOptions);

	[Benchmark]
	[BenchmarkCategory("Serialization")]
	public OrderAggregate? Serialization_SnapshotDeserialize() =>
		JsonSerializer.Deserialize<OrderAggregate>(_snapshotJson, EventStoreSerializationHelpers.JsonSerializerOptions);

	[Benchmark]
	[BenchmarkCategory("Serialization")]
	public OrderAggregate? Serialization_SnapshotRoundTrip()
	{
		var json = JsonSerializer.Serialize(_snapshotAggregate, EventStoreSerializationHelpers.JsonSerializerOptions);
		return JsonSerializer.Deserialize<OrderAggregate>(json, EventStoreSerializationHelpers.JsonSerializerOptions);
	}

	[Benchmark]
	[BenchmarkCategory("Serialization")]
	public string Serialization_ValueObjectSerialize() => EventStoreSerializationHelpers.Serialize(_moneyA);

	[Benchmark]
	[BenchmarkCategory("Serialization")]
	public Money Serialization_ValueObjectDeserialize() =>
		EventStoreSerializationHelpers.Deserialize<Money>(_moneyJson);

	[Benchmark]
	[BenchmarkCategory("Serialization")]
	public Money Serialization_ValueObjectRoundTrip()
	{
		var json = EventStoreSerializationHelpers.Serialize(_moneyA);
		return EventStoreSerializationHelpers.Deserialize<Money>(json);
	}

	[Benchmark]
	[BenchmarkCategory("Collections")]
	public EventStoreList<int> Collections_ListAdd()
	{
		var list = new EventStoreList<int>();
		((ICollection<int>)list).Add(1);
		return list;
	}

	[Benchmark]
	[BenchmarkCategory("Collections")]
	public int Collections_ListEnumerate()
	{
		var count = 0;
		foreach (var item in _list)
			count += item;
		return count;
	}

	[Benchmark]
	[BenchmarkCategory("Collections")]
	public EventStoreSet<int> Collections_SetAdd()
	{
		var set = new EventStoreSet<int>();
		((ICollection<int>)set).Add(1);
		return set;
	}

	[Benchmark]
	[BenchmarkCategory("Collections")]
	public bool Collections_SetContains() => _set.Contains(50);

	[Benchmark]
	[BenchmarkCategory("Collections")]
	public bool Collections_SetRemove()
	{
		var set = new EventStoreSet<int>([1, 2, 3]);
		return ((ICollection<int>)set).Remove(2);
	}

	[Benchmark]
	[BenchmarkCategory("Mapping")]
	public string EventNameMapper_GetName() => _mapper.GetName<OrderAggregate>(typeof(OrderCreatedEvent));

	static OrderAggregate CreatePopulatedOrder()
	{
		var aggregate = new OrderAggregate();
		aggregate.CreateOrder("customer-1");
		aggregate.SetShippingAddress("123 Main St");
		aggregate.UpdateNotes("order note");
		aggregate.AddLineItem(
			new EventStoreList<OrderLineItem>([new OrderLineItem("p1", "Product 1", 2, 9.99m)]),
			19.98m
		);
		aggregate.ConfirmOrder();
		aggregate.ShipOrder();
		aggregate.CompleteOrder();
		return aggregate;
	}
}
