namespace Purview.EventSourcing.SourceGenerator.Manifest;

public sealed class EventContractCompatibilityTests : AggregateSourceGeneratorTestBase
{
	const string OrderAggregateWithTwoEvents = """
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

	const string OrderAggregateSingleEvent = """
		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }
				public decimal Total { get; private set; }

				[Event]
				public partial void CreateOrder(string customerId, decimal total);
			}
		}
		""";

	const string OrderAggregateRemovedField = """
		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }
				public decimal Total { get; private set; }

				[Event]
				public partial void CreateOrder(string customerId);
			}
		}
		""";

	const string OrderAggregateChangedFieldType = """
		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public int CustomerId { get; private set; }

				[Event]
				public partial void CreateOrder(int customerId);
			}
		}
		""";

	const string MinimalOrderAggregate = """
		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }

				[Event]
				public partial void CreateOrder(string customerId);
			}
		}
		""";

	const string OrderAggregateVersionOne = """
		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }

				[Event(Version = 1)]
				public partial void CreateOrder(string customerId);
			}
		}
		""";

	const string OrderAggregateVersionTwo = """
		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }
				public decimal Total { get; private set; }

				[Event(Version = 2)]
				public partial void CreateOrder(string customerId, decimal total);
			}
		}
		""";

	[Test]
	public async Task Generate_GivenRemovedAggregate_ReportsEventContractRemoved(CancellationToken cancellationToken)
	{
		var baselineJson = await GenerateBaselineAsync(MinimalOrderAggregate, cancellationToken);

		const string removedAggregateSource = """
			namespace Testing
			{
				public class NotAnAggregate
				{
				}
			}
			""";

		using var result = await GenerateAsync(
			removedAggregateSource,
			ManifestTestHelpers.WithBaseline(baselineJson),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE030");
	}

	[Test]
	public async Task Generate_GivenRemovedEvent_ReportsEventContractEventRemoved(CancellationToken cancellationToken)
	{
		var baselineJson = await GenerateBaselineAsync(OrderAggregateWithTwoEvents, cancellationToken);

		using var result = await GenerateAsync(
			OrderAggregateSingleEvent,
			ManifestTestHelpers.WithBaseline(baselineJson),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE031");
	}

	[Test]
	public async Task Generate_GivenRemovedField_ReportsEventContractFieldRemoved(CancellationToken cancellationToken)
	{
		var baselineJson = await GenerateBaselineAsync(OrderAggregateWithTwoEvents, cancellationToken);

		using var result = await GenerateAsync(
			OrderAggregateRemovedField,
			ManifestTestHelpers.WithBaseline(baselineJson),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE032");
	}

	[Test]
	public async Task Generate_GivenChangedFieldType_ReportsEventContractFieldTypeChanged(
		CancellationToken cancellationToken
	)
	{
		var baselineJson = await GenerateBaselineAsync(OrderAggregateWithTwoEvents, cancellationToken);

		using var result = await GenerateAsync(
			OrderAggregateChangedFieldType,
			ManifestTestHelpers.WithBaseline(baselineJson),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE033");
	}

	[Test]
	public async Task Generate_GivenFieldBecameNonNullable_ReportsEventContractFieldBecameRequired(
		CancellationToken cancellationToken
	)
	{
		const string nullableOriginal = """
			#nullable enable
			namespace Testing
			{
				[Aggregate]
				public partial class OrderAggregate : AggregateBase
				{
					public string? CustomerId { get; private set; }

					[Event]
					public partial void CreateOrder(string? customerId);
				}
			}
			""";

		const string nonNullableCurrent = """
			#nullable enable
			namespace Testing
			{
				[Aggregate]
				public partial class OrderAggregate : AggregateBase
				{
					public string CustomerId { get; private set; } = string.Empty;

					[Event]
					public partial void CreateOrder(string customerId);
				}
			}
			""";

		using var baselineRun = await GenerateAsync(
			nullableOriginal,
			ManifestTestHelpers.WithManifestEnabledAndNullableContext(),
			cancellationToken
		);
		var baselineJson = ManifestTestHelpers.ExtractManifestJson(ManifestTestHelpers.GetManifestSource(baselineRun));

		using var result = await GenerateAsync(
			nonNullableCurrent,
			ManifestTestHelpers.WithBaselineAndNullableContext(baselineJson),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE034");
	}

	[Test]
	public async Task Generate_GivenFieldBecameRequired_ReportsEventContractFieldBecameRequired(
		CancellationToken cancellationToken
	)
	{
		const string requiredCurrent = """
			namespace Testing
			{
				[Aggregate]
				public partial class OrderAggregate : AggregateBase
				{
					public string CustomerId { get; private set; }

					[Event]
					public partial void CreateOrder([System.ComponentModel.DataAnnotations.Required] string customerId);
				}
			}
			""";

		var baselineJson = await GenerateBaselineAsync(MinimalOrderAggregate, cancellationToken);

		using var result = await GenerateAsync(
			requiredCurrent,
			ManifestTestHelpers.WithBaseline(baselineJson),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE034");
	}

	[Test]
	public async Task Generate_GivenAddedRequiredField_ReportsEventContractFieldBecameRequired(
		CancellationToken cancellationToken
	)
	{
		const string addedRequiredField = """
			namespace Testing
			{
				[Aggregate]
				public partial class OrderAggregate : AggregateBase
				{
					public string CustomerId { get; private set; }
					public string OrderCode { get; private set; }

					[Event]
					public partial void CreateOrder(
						string customerId,
						[System.ComponentModel.DataAnnotations.Required] string orderCode);
				}
			}
			""";

		var baselineJson = await GenerateBaselineAsync(MinimalOrderAggregate, cancellationToken);

		using var result = await GenerateAsync(
			addedRequiredField,
			ManifestTestHelpers.WithBaseline(baselineJson),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE034");
	}

	[Test]
	public async Task Generate_GivenSchemaVersionRegression_ReportsEventContractSchemaVersionRegression(
		CancellationToken cancellationToken
	)
	{
		var baselineJson = await GenerateBaselineAsync(OrderAggregateVersionTwo, cancellationToken);

		using var result = await GenerateAsync(
			OrderAggregateVersionOne,
			ManifestTestHelpers.WithBaseline(baselineJson),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE035");
	}

	[Test]
	public async Task Generate_GivenMalformedBaseline_ReportsEventContractBaselineMalformed(
		CancellationToken cancellationToken
	)
	{
		using var result = await GenerateAsync(
			OrderAggregateSingleEvent,
			ManifestTestHelpers.WithBaseline("{ this is not valid json"),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE036");
	}

	[Test]
	public async Task Generate_GivenUnsupportedBaselineFormatVersion_ReportsEventContractBaselineMalformed(
		CancellationToken cancellationToken
	)
	{
		const string unsupportedVersion = /*lang=json,strict*/
			"""{"formatVersion":99,"aggregates":[]}""";

		using var result = await GenerateAsync(
			OrderAggregateSingleEvent,
			ManifestTestHelpers.WithBaseline(unsupportedVersion),
			cancellationToken
		);

		await Assert.That(result).HasDiagnostic("EVENTSTORE036");
	}

	[Test]
	public async Task Generate_GivenAddedOptionalField_ReportsNoCompatibilityDiagnostics(
		CancellationToken cancellationToken
	)
	{
		const string addedOptionalField = """
			#nullable enable
			namespace Testing
			{
				[Aggregate]
				public partial class OrderAggregate : AggregateBase
				{
					public string CustomerId { get; private set; } = string.Empty;
					public string? OrderCode { get; private set; }

					[Event]
					public partial void CreateOrder(string customerId, string? orderCode);
				}
			}
			""";

		const string minimalNullableBaseline = """
			#nullable enable
			namespace Testing
			{
				[Aggregate]
				public partial class OrderAggregate : AggregateBase
				{
					public string CustomerId { get; private set; } = string.Empty;

					[Event]
					public partial void CreateOrder(string customerId);
				}
			}
			""";

		using var baselineRun = await GenerateAsync(
			minimalNullableBaseline,
			ManifestTestHelpers.WithManifestEnabledAndNullableContext(),
			cancellationToken
		);
		var baselineJson = ManifestTestHelpers.ExtractManifestJson(ManifestTestHelpers.GetManifestSource(baselineRun));

		using var result = await GenerateAsync(
			addedOptionalField,
			ManifestTestHelpers.WithBaselineAndNullableContext(baselineJson),
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

	[Test]
	public async Task Generate_GivenVersionBump_ReportsNoCompatibilityDiagnostics(CancellationToken cancellationToken)
	{
		var baselineJson = await GenerateBaselineAsync(OrderAggregateVersionOne, cancellationToken);

		using var result = await GenerateAsync(
			OrderAggregateVersionTwo,
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

	[Test]
	public async Task Generate_GivenAddedEvent_ReportsNoCompatibilityDiagnostics(CancellationToken cancellationToken)
	{
		var baselineJson = await GenerateBaselineAsync(MinimalOrderAggregate, cancellationToken);

		using var result = await GenerateAsync(
			OrderAggregateWithTwoEvents,
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

	async Task<string> GenerateBaselineAsync(string source, CancellationToken cancellationToken)
	{
		using var result = await GenerateAsync(source, ManifestTestHelpers.WithManifestEnabled(), cancellationToken);
		return ManifestTestHelpers.ExtractManifestJson(ManifestTestHelpers.GetManifestSource(result));
	}
}
