using System.Text.Json;

namespace Purview.EventSourcing.SourceGenerator.Manifest;

public sealed class EventContractManifestTests : AggregateSourceGeneratorTestBase
{
	const string OrderAggregateSource = """
		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }
				public decimal Total { get; private set; }

				[Event]
				public partial void CreateOrder(string customerId, decimal total);

				[Event]
				public partial void UpdateTotal(decimal total);
			}
		}
		""";

	const string ReorderedAggregateSource = """
		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }
				public decimal Total { get; private set; }

				[Event]
				public partial void UpdateTotal(decimal total);

				[Event]
				public partial void CreateOrder(string customerId, decimal total);
			}
		}
		""";

	const string RichAggregateSource = """
		#nullable enable

		namespace Sales
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

			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; } = string.Empty;
				public OrderStatus Status { get; private set; } = OrderStatus.Hydrate(OrderStatusCode.Draft);
				public Purview.EventSourcing.EventStoreSet<string> Tags { get; private set; } = new();

				[Event(EventName = "OrderConfirmed", Version = 3, EventNamespace = "Sales.Events.Internal")]
				public partial void ConfirmOrder(OrderStatusCode status);

				[Event]
				public partial void UpdateTags(Purview.EventSourcing.EventStoreSet<string> tags);
			}
		}

		namespace Billing
		{
			[Purview.ValueObjects.Serialization.Scalar]
			public readonly partial record struct Amount
			{
				public decimal Value { get; }
				private Amount(decimal value) => Value = value;
				public static Amount Create(decimal value, in Purview.ValueObjects.ValueObjectContext<InvoiceAggregate> context) => new(value);
				public static Amount Hydrate(decimal value) => new(value);
			}

			[Aggregate]
			public partial class InvoiceAggregate : AggregateBase
			{
				public string? Reference { get; private set; }
				public Amount Total { get; private set; } = Amount.Hydrate(0m);

				[Event(EventName = "InvoiceRaised", Version = 2)]
				public partial void RaiseInvoice(string? reference, decimal total);
			}
		}
		""";

	[Test]
	public async Task Generate_WithManifestEnabled_EmitsDeterministicManifest(CancellationToken cancellationToken)
	{
		var first = await GenerateAsync(
			OrderAggregateSource,
			ManifestTestHelpers.WithManifestEnabled(),
			cancellationToken
		);
		var second = await GenerateAsync(
			OrderAggregateSource,
			ManifestTestHelpers.WithManifestEnabled(),
			cancellationToken
		);

		var firstJson = ManifestTestHelpers.ExtractManifestJson(ManifestTestHelpers.GetManifestSource(first));
		var secondJson = ManifestTestHelpers.ExtractManifestJson(ManifestTestHelpers.GetManifestSource(second));

		await Assert.That(firstJson).IsNotEmpty();
		await Assert.That(secondJson).IsEqualTo(firstJson);

		using var document = JsonDocument.Parse(firstJson);
		var root = document.RootElement;
		await Assert.That(root.GetProperty("formatVersion").GetInt32()).IsEqualTo(1);

		var aggregate = root.GetProperty("aggregates").EnumerateArray().Single();
		await Assert.That(aggregate.GetProperty("name").GetString()).IsEqualTo("OrderAggregate");

		var events = aggregate.GetProperty("events").EnumerateArray().ToArray();
		await Assert.That(events.Length).IsEqualTo(2);

		var createOrder = events.Single(entry => entry.GetProperty("method").GetString() == "CreateOrder");
		await Assert.That(createOrder.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
		await Assert
			.That(
				createOrder
					.GetProperty("fields")
					.EnumerateArray()
					.Select(field => field.GetProperty("name").GetString()!)
			)
			.IsEquivalentTo(["CustomerId", "Total"]);
	}

	[Test]
	public async Task Generate_WithoutManifestProperty_DoesNotEmitManifest(CancellationToken cancellationToken)
	{
		var result = await GenerateAsync(OrderAggregateSource, cancellationToken);

		await Assert.That(ManifestTestHelpers.GetManifestSource(result)).IsEmpty();
	}

	[Test]
	public async Task Generate_ManifestOrderingIsIndependentOfDeclarationOrder(CancellationToken cancellationToken)
	{
		var ordered = await GenerateAsync(
			OrderAggregateSource,
			ManifestTestHelpers.WithManifestEnabled(),
			cancellationToken
		);
		var reordered = await GenerateAsync(
			ReorderedAggregateSource,
			ManifestTestHelpers.WithManifestEnabled(),
			cancellationToken
		);

		var orderedJson = ManifestTestHelpers.ExtractManifestJson(ManifestTestHelpers.GetManifestSource(ordered));
		var reorderedJson = ManifestTestHelpers.ExtractManifestJson(ManifestTestHelpers.GetManifestSource(reordered));

		await Assert.That(reorderedJson).IsEqualTo(orderedJson);
	}

	[Test]
	public async Task Generate_GivenRichContracts_ManifestCapturesIdentityVersionsAndFieldShapes(
		CancellationToken cancellationToken
	)
	{
		var result = await GenerateAsync(
			RichAggregateSource,
			ManifestTestHelpers.WithManifestEnabled(),
			cancellationToken
		);

		var json = ManifestTestHelpers.ExtractManifestJson(ManifestTestHelpers.GetManifestSource(result));
		using var document = JsonDocument.Parse(json);
		var aggregates = document.RootElement.GetProperty("aggregates").EnumerateArray().ToArray();

		await Assert.That(aggregates.Length).IsEqualTo(2);
		await Assert
			.That(aggregates.Select(a => a.GetProperty("name").GetString()!))
			.IsEquivalentTo(["InvoiceAggregate", "OrderAggregate"]);

		var order = aggregates.Single(a => a.GetProperty("name").GetString() == "OrderAggregate");
		var events = order.GetProperty("events").EnumerateArray().ToArray();

		var confirmed = events.Single(entry => entry.GetProperty("name").GetString() == "OrderConfirmed");
		await Assert.That(confirmed.GetProperty("schemaVersion").GetInt32()).IsEqualTo(3);
		await Assert.That(confirmed.GetProperty("namespace").GetString()).IsEqualTo("Sales.Events.Internal");

		var replaceTags = events.Single(entry => entry.GetProperty("method").GetString() == "UpdateTags");
		var tagField = replaceTags.GetProperty("fields").EnumerateArray().Single();
		await Assert.That(tagField.GetProperty("name").GetString()).IsEqualTo("Tags");
		await Assert.That(tagField.GetProperty("type").GetString()).Contains("EventStoreSet");

		var invoice = aggregates.Single(a => a.GetProperty("name").GetString() == "InvoiceAggregate");
		var raiseInvoice = invoice.GetProperty("events").EnumerateArray().Single();
		await Assert.That(raiseInvoice.GetProperty("schemaVersion").GetInt32()).IsEqualTo(2);

		var referenceField = raiseInvoice
			.GetProperty("fields")
			.EnumerateArray()
			.ToArray()
			.Single(field => field.GetProperty("name").GetString() == "Reference");
		await Assert.That(referenceField.GetProperty("isNullable").GetBoolean()).IsTrue();

		var totalField = raiseInvoice
			.GetProperty("fields")
			.EnumerateArray()
			.ToArray()
			.Single(field => field.GetProperty("name").GetString() == "Total");
		await Assert.That(totalField.GetProperty("type").GetString()).Contains("Amount");
	}

	[Test]
	public async Task Generate_UnchangedContractsAgainstOwnBaseline_ReportsNoCompatibilityDiagnostics(
		CancellationToken cancellationToken
	)
	{
		var baselineRun = await GenerateAsync(
			RichAggregateSource,
			ManifestTestHelpers.WithManifestEnabled(),
			cancellationToken
		);
		var baselineJson = ManifestTestHelpers.ExtractManifestJson(ManifestTestHelpers.GetManifestSource(baselineRun));

		var result = await GenerateAsync(
			RichAggregateSource,
			ManifestTestHelpers.WithBaseline(baselineJson),
			cancellationToken
		);

		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE030");
		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE031");
		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE032");
		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE033");
		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE034");
		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE035");
		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE036");
	}
}
