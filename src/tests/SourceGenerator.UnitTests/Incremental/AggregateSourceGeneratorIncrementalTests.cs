using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using StepReason = Microsoft.CodeAnalysis.IncrementalStepRunReason;

namespace Purview.EventSourcing.SourceGenerator.Incremental;

/// <summary>
/// Per-aggregate incremental caching tests driven by the framework's <c>GenerateIncrementalAsync</c>
/// runner, which reuses one <c>GeneratorDriver</c> across the supplied source sets so each run's
/// step reasons prove what actually changed.
/// </summary>
public sealed class AggregateSourceGeneratorIncrementalTests : AggregateSourceGeneratorTestBase
{
	const string OrderAggregateSource = """
		using Purview.EventSourcing.Aggregates;

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

	const string ModifiedOrderAggregateSource = """
		using Purview.EventSourcing.Aggregates;

		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }
				public decimal Total { get; private set; }

				[Event]
				public partial void CreateOrder(string customerId);

				[Event]
				public partial void UpdateTotal(decimal total);
			}
		}
		""";

	const string CustomerAggregateSource = """
		using Purview.EventSourcing.Aggregates;

		namespace Testing
		{
			[Aggregate]
			public partial class CustomerAggregate : AggregateBase
			{
				public string Name { get; private set; }

				[Event]
				public partial void RegisterCustomer(string name);
			}
		}
		""";

	[Test]
	public async Task Generate_FirstRun_AllAggregateTargetsNew(CancellationToken cancellationToken)
	{
		var result = await GenerateIncrementalAsync(
			[new IncrementalRunInput([OrderAggregateSource, CustomerAggregateSource])],
			cancellationToken: cancellationToken
		);

		var reasons = StepReasons(result.Runs[0], "GetAggregateTargets");
		await Assert.That(reasons.Length).IsEqualTo(2);
		await Assert.That(reasons.All(static reason => reason == StepReason.New)).IsTrue();
	}

	[Test]
	public async Task Generate_RerunWithUnchangedCompilation_AggregateTargetsCached(CancellationToken cancellationToken)
	{
		var result = await GenerateIncrementalAsync(
			[OrderAggregateSource, CustomerAggregateSource],
			cancellationToken: cancellationToken
		);

		var reasons = StepReasons(result.Runs[1], "GetAggregateTargets");
		await Assert.That(reasons.Length).IsEqualTo(2);
		await Assert.That(reasons.All(static reason => reason == StepReason.Cached)).IsTrue();
	}

	[Test]
	public async Task Generate_GivenChangeToOneAggregate_OnlyThatTargetModified(CancellationToken cancellationToken)
	{
		var result = await GenerateIncrementalAsync(
			[
				new IncrementalRunInput([OrderAggregateSource, CustomerAggregateSource]),
				new IncrementalRunInput([ModifiedOrderAggregateSource, CustomerAggregateSource]),
			],
			cancellationToken: cancellationToken
		);

		var steps = GetSteps(result.Runs[1], "GetAggregateTargets");
		var orderStep = steps.Single(step => GetAggregateName(step) == "OrderAggregate");
		var customerStep = steps.Single(step => GetAggregateName(step) == "CustomerAggregate");

		await Assert.That(GetReason(orderStep)).IsEqualTo(StepReason.Modified);
		await Assert.That(GetReason(customerStep)).IsNotEqualTo(StepReason.Modified);
	}

	[Test]
	public async Task Generate_GivenAggregateDeleted_TargetRemoved(CancellationToken cancellationToken)
	{
		var result = await GenerateIncrementalAsync(
			[
				new IncrementalRunInput([OrderAggregateSource, CustomerAggregateSource]),
				new IncrementalRunInput([OrderAggregateSource]),
			],
			cancellationToken: cancellationToken
		);

		var steps = GetSteps(result.Runs[1], "GetAggregateTargets");
		await Assert.That(steps.Length).IsEqualTo(2);
		await Assert.That(steps.Count(step => GetReason(step) == StepReason.Removed)).IsEqualTo(1);

		var orderStep = steps.Single(step =>
			GetReason(step) != StepReason.Removed && GetAggregateName(step) == "OrderAggregate"
		);
		await Assert.That(GetReason(orderStep)).IsNotEqualTo(StepReason.Modified);
	}

	[Test]
	public async Task Generate_GivenAggregateAdded_OnlyNewTargetAdded(CancellationToken cancellationToken)
	{
		const string additionalAggregateSource = """
			using Purview.EventSourcing.Aggregates;

			namespace Testing
			{
				[Aggregate]
				public partial class InventoryAggregate : AggregateBase
				{
					public int Quantity { get; private set; }

					[Event]
					public partial void AdjustQuantity(int quantity);
				}
			}
			""";

		var result = await GenerateIncrementalAsync(
			[
				new IncrementalRunInput([OrderAggregateSource, CustomerAggregateSource]),
				new IncrementalRunInput([OrderAggregateSource, CustomerAggregateSource, additionalAggregateSource]),
			],
			cancellationToken: cancellationToken
		);

		var steps = GetSteps(result.Runs[1], "GetAggregateTargets");
		await Assert.That(steps.Length).IsEqualTo(3);
		await Assert.That(steps.Count(step => GetReason(step) == StepReason.New)).IsEqualTo(1);

		var inventoryStep = steps.Single(step => GetAggregateName(step) == "InventoryAggregate");
		await Assert.That(GetReason(inventoryStep)).IsEqualTo(StepReason.New);

		var orderStep = steps.Single(step => GetAggregateName(step) == "OrderAggregate");
		var customerStep = steps.Single(step => GetAggregateName(step) == "CustomerAggregate");
		await Assert.That(GetReason(orderStep)).IsNotEqualTo(StepReason.Modified);
		await Assert.That(GetReason(customerStep)).IsNotEqualTo(StepReason.Modified);
	}

	[Test]
	public async Task Generate_GivenChangeToOneAggregate_OtherAggregateOutputUnchanged(
		CancellationToken cancellationToken
	)
	{
		var result = await GenerateIncrementalAsync(
			[
				new IncrementalRunInput([OrderAggregateSource, CustomerAggregateSource]),
				new IncrementalRunInput([ModifiedOrderAggregateSource, CustomerAggregateSource]),
			],
			cancellationToken: cancellationToken
		);

		var firstCustomerSource = GeneratedSourcesContaining(result.Runs[0].RunResult, "CustomerAggregate");
		var secondCustomerSource = GeneratedSourcesContaining(result.Runs[1].RunResult, "CustomerAggregate");
		await Assert.That(secondCustomerSource).IsEqualTo(firstCustomerSource);
	}

	[Test]
	public async Task Generate_GivenInvalidPartialCode_ThenRecovery_RegeneratesAndCaches(
		CancellationToken cancellationToken
	)
	{
		const string nonPartialAggregateSource = """
			using Purview.EventSourcing.Aggregates;

			namespace Testing
			{
				[Aggregate]
				public class OrderAggregate : AggregateBase
				{
					public string CustomerId { get; private set; }

					[Event]
					public partial void CreateOrder(string customerId);
				}
			}
			""";

		var result = await GenerateIncrementalAsync(
			[
				new IncrementalRunInput([nonPartialAggregateSource]),
				new IncrementalRunInput([nonPartialAggregateSource]),
				new IncrementalRunInput([OrderAggregateSource]),
			],
			cancellationToken: cancellationToken
		);

		await Assert.That(AggregateSources(result.Runs[0].RunResult)).IsEmpty();

		var invalidRerunSteps = GetSteps(result.Runs[1], "GetAggregateTargets");
		await Assert
			.That(invalidRerunSteps.All(step => GetReason(step) is StepReason.Cached or StepReason.Unchanged))
			.IsTrue();

		await Assert.That(AggregateSources(result.Runs[2].RunResult)).IsNotEmpty();
	}

	static ImmutableArray<IncrementalGeneratorRunStep> GetSteps(IncrementalCacheRun run, string stepName) =>
		run.Steps.TryGetValue(stepName, out var steps) ? steps : [];

	static ImmutableArray<StepReason> StepReasons(IncrementalCacheRun run, string stepName) =>
		[.. GetSteps(run, stepName).SelectMany(static step => step.Outputs.Select(static output => output.Reason))];

	static StepReason GetReason(IncrementalGeneratorRunStep step) =>
		step.Outputs.Length > 0 ? step.Outputs[0].Reason : default;

	static string GetAggregateName(IncrementalGeneratorRunStep step)
	{
		if (
			step.Outputs.Length > 0
			&& step.Outputs[0].Value is SourceGeneratorFramework.GeneratorResult<AggregateTarget> result
		)
			return result.HasValue ? result.Value.Info.AggregateClass.Identity.Name : "(failed)";

		// If the step has no outputs, we can't determine the aggregate name, so return a placeholder.
		return "(unknown)";
	}

	static ImmutableArray<GeneratedSourceResult> AggregateSources(GeneratorRunResult result) =>
		[
			.. result.GeneratedSources.Where(static source =>
				!EventSourcingGeneratorTestOptions.AggregateGeneratedAttributes.Contains(
					source.HintName,
					StringComparer.Ordinal
				)
				&& source.HintName != EventSourcingGeneratorTestOptions.PreCompilationMarkerHintName
			),
		];

	static string GeneratedSourcesContaining(GeneratorRunResult result, string fragment) =>
		result
			.GeneratedSources.Select(static source => source.SourceText.ToString())
			.Single(source => source.Contains(fragment, StringComparison.Ordinal));
}
