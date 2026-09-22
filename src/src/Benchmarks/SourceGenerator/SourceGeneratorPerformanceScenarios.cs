using Microsoft.CodeAnalysis;
using Purview.EventSourcing.SourceGenerator.Generators;

namespace Purview.EventSourcing.Benchmarks.SourceGenerator;

public sealed record SourceGeneratorScenario(
	string Name,
	string GeneratorName,
	string Source,
	Func<IIncrementalGenerator> CreateGenerator,
	string? EditedSource = null
)
{
	public override string ToString() => Name;
}

static class SourceGeneratorPerformanceScenarios
{
	public static IReadOnlyList<SourceGeneratorScenario> All { get; } =
	[
		new(
			"AggregateSimple",
			nameof(AggregateSourceGenerator),
			SharedStubs.All + Samples.AggregateSimple,
			static () => new AggregateSourceGenerator()
		),
		new(
			"AggregateWithValueObjects",
			nameof(AggregateSourceGenerator),
			SharedStubs.All + Samples.AggregateWithValueObjects,
			static () => new AggregateSourceGenerator()
		),
		new(
			"AggregateMulti",
			nameof(AggregateSourceGenerator),
			SharedStubs.All + Samples.AggregateMulti,
			static () => new AggregateSourceGenerator(),
			EditedSource: SharedStubs.All + Samples.AggregateMultiEdited
		),
	];

	static class SharedStubs
	{
		public const string All = """
			#nullable enable

			namespace Purview.EventSourcing.Aggregates
			{
				public interface IAggregate { }

				[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
				public sealed class AggregateAttribute : System.Attribute
				{
					public string? EventNamespace { get; set; }
				}

				[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
				public sealed class EventAttribute : System.Attribute
				{
					public int Version { get; set; } = 1;
					public string? EventName { get; set; }
					public string? EventNamespace { get; set; }
				}

				public sealed class AggregateDetails
				{
					public string? Id { get; set; }
				}

				public abstract class AggregateBase : IAggregate
				{
					readonly System.Collections.Generic.Dictionary<System.Type, System.Delegate> _appliers = new();

					protected AggregateBase()
					{
						RegisterEvents();
					}

					public AggregateDetails Details { get; init; } = new();
					protected abstract void RegisterEvents();
					protected void Register<TEvent>(System.Action<TEvent> applier) where TEvent : class => _appliers[typeof(TEvent)] = applier;
					protected void RegisterGenerated<TEvent>() where TEvent : class { }
					protected AggregateBase RecordAndApply<TEvent>(TEvent @event) where TEvent : class
					{
						((System.Action<TEvent>)_appliers[typeof(TEvent)])(@event);
						return this;
					}
				}
			}

			namespace Purview.EventSourcing.Aggregates.Events
			{
				[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
				public sealed class EventContractAttribute : System.Attribute { }

				public readonly record struct EventMetadata(
					int AggregateVersion,
					System.DateTimeOffset When,
					int SchemaVersion,
					string? IdempotencyId,
					string? CorrelationId,
					string? CausationId,
					string? UserId);
			}

			namespace Purview.EventSourcing.Aggregates
			{
				public readonly record struct EventRecord(object Event, Purview.EventSourcing.Aggregates.Events.EventMetadata Metadata);
			}

			namespace Purview.ValueObjects
			{
				public readonly record struct ValueObjectContext<TOwner>(
					TOwner Owner,
					string MemberName,
					string? Reason = null
				);

				public interface IValueObject { }
				public interface IValueObject<TSelf> : IValueObject, System.IComparable<TSelf>, System.IComparable where TSelf : IValueObject<TSelf> { }
				public interface IScalarValueObject<TSelf, TValue> : IValueObject, System.IComparable<TSelf>, System.IComparable where TSelf : IScalarValueObject<TSelf, TValue>
				{
					TValue Value { get; }
					int CompareTo(TValue other);
					static abstract TSelf Create(TValue value);
					static abstract TSelf Hydrate(TValue value);
				}
			}

			namespace Purview.ValueObjects.Serialization
			{
				public enum ValueObjectDeserializationMode
				{
					Hydrate,
					Strict
				}

				[System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.Class)]
				public sealed class ScalarAttribute : System.Attribute
				{
					public ScalarAttribute(string propertyName = "Value") => PropertyName = propertyName;
					public string PropertyName { get; }
					public bool GenerateJsonConverter { get; init; } = true;
					public bool GenerateComparable { get; init; } = true;
					public bool GenerateComparisonOperators { get; init; } = true;
					public bool GenerateImplicitFromPrimitive { get; init; } = true;
					public bool GenerateImplicitToPrimitive { get; init; } = true;
					public ValueObjectDeserializationMode DeserializationMode { get; init; } = ValueObjectDeserializationMode.Hydrate;
				}

				[System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.Class)]
				public sealed class ValueObjectAttribute : System.Attribute
				{
					public bool GenerateJsonConverter { get; init; } = true;
					public bool GenerateComparable { get; init; } = true;
					public bool GenerateComparisonOperators { get; init; } = true;
					public ValueObjectDeserializationMode DeserializationMode { get; init; } = ValueObjectDeserializationMode.Hydrate;
				}
			}
			""";
	}

	static class Samples
	{
		public const string AggregateSimple = """
			namespace Testing
			{
				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class OrderAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public string CustomerId { get; private set; } = string.Empty;
					public decimal Total { get; private set; }

					[Purview.EventSourcing.Aggregates.Event]
					public partial void CreateOrder(string customerId, decimal total);

					[Purview.EventSourcing.Aggregates.Event]
					public partial void UpdateTotal(decimal total);
				}
			}
			""";

		public const string AggregateWithValueObjects = """
			namespace Testing
			{
				public enum OrderStatusCode
				{
					Draft = 0,
					Confirmed = 1
				}

				[Purview.ValueObjects.Serialization.Scalar]
				public readonly partial record struct OrderStatus
				{
					public OrderStatusCode Value { get; }
					private OrderStatus(OrderStatusCode value) => Value = value;
					public static OrderStatus Create(OrderStatusCode value, in Purview.ValueObjects.ValueObjectContext<OrderAggregate> context) => new(value);
					public static OrderStatus Hydrate(OrderStatusCode value) => new(value);
				}

				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class OrderAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public OrderStatus Status { get; private set; } = OrderStatus.Hydrate(OrderStatusCode.Draft);
					public int LineItems { get; private set; }

					[Purview.EventSourcing.Aggregates.Event(EventName = "OrderConfirmed")]
					public partial void ConfirmOrder(OrderStatusCode status);
				}
			}
			""";

		public const string AggregateMulti = """
			namespace Testing
			{
				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class OrderAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public string CustomerId { get; private set; } = string.Empty;
					public decimal Total { get; private set; }

					[Purview.EventSourcing.Aggregates.Event]
					public partial void CreateOrder(string customerId, decimal total);

					[Purview.EventSourcing.Aggregates.Event]
					public partial void UpdateTotal(decimal total);
				}

				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class CustomerAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public string Name { get; private set; } = string.Empty;

					[Purview.EventSourcing.Aggregates.Event]
					public partial void RegisterCustomer(string name);
				}

				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class InventoryAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public int Quantity { get; private set; }

					[Purview.EventSourcing.Aggregates.Event]
					public partial void AdjustQuantity(int quantity);
				}

				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class InvoiceAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public decimal Amount { get; private set; }

					[Purview.EventSourcing.Aggregates.Event]
					public partial void RaiseInvoice(decimal amount);
				}

				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class ShipmentAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public string TrackingCode { get; private set; } = string.Empty;

					[Purview.EventSourcing.Aggregates.Event]
					public partial void RegisterShipment(string trackingCode);
				}
			}
			""";

		public const string AggregateMultiEdited = """
			#nullable enable
			namespace Testing
			{
				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class OrderAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public string CustomerId { get; private set; } = string.Empty;
					public decimal Total { get; private set; }
					public string? PromotionCode { get; private set; }

					[Purview.EventSourcing.Aggregates.Event]
					public partial void CreateOrder(string customerId, decimal total);

					[Purview.EventSourcing.Aggregates.Event]
					public partial void UpdateTotal(decimal total);

					[Purview.EventSourcing.Aggregates.Event]
					public partial void ApplyPromotion(string? promotionCode);
				}

				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class CustomerAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public string Name { get; private set; } = string.Empty;

					[Purview.EventSourcing.Aggregates.Event]
					public partial void RegisterCustomer(string name);
				}

				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class InventoryAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public int Quantity { get; private set; }

					[Purview.EventSourcing.Aggregates.Event]
					public partial void AdjustQuantity(int quantity);
				}

				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class InvoiceAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public decimal Amount { get; private set; }

					[Purview.EventSourcing.Aggregates.Event]
					public partial void RaiseInvoice(decimal amount);
				}

				[Purview.EventSourcing.Aggregates.Aggregate]
				public partial class ShipmentAggregate : Purview.EventSourcing.Aggregates.AggregateBase
				{
					public string TrackingCode { get; private set; } = string.Empty;

					[Purview.EventSourcing.Aggregates.Event]
					public partial void RegisterShipment(string trackingCode);
				}
			}
			""";
	}
}
