using System.Collections.Immutable;
using StepReason = Microsoft.CodeAnalysis.IncrementalStepRunReason;

namespace Purview.EventSourcing.SourceGenerator.Incremental;

/// <summary>
/// Stage-by-stage incremental caching tests driven by the framework's
/// <c>GenerateIncrementalAsync</c> runner, which reuses one <c>GeneratorDriver</c> and one
/// <c>Compilation</c> across identical runs so caching behavior is asserted meaningfully.
/// </summary>
public sealed class AggregateIncrementalCacheTests : AggregateSourceGeneratorTestBase
{
	const string Source = """
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

	const string ChangedSource = """
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
			}
		}
		""";

	static ImmutableDictionary<string, ImmutableArray<StepReason>> StepReasons(IncrementalCacheRun run)
	{
		var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<StepReason>>();
		foreach (var pair in run.Steps)
			builder[pair.Key] =
			[
				.. pair.Value.SelectMany(static step => step.Outputs.Select(static output => output.Reason)),
			];
		return builder.ToImmutable();
	}

	static string[] GeneratedSources(IncrementalCacheRun run) =>
		[
			.. run
				.RunResult.GeneratedSources.Select(static source => source.SourceText.ToString())
				.OrderBy(static source => source, StringComparer.Ordinal),
		];

	[Test]
	public async Task IdenticalRerun_AllStagesCached(CancellationToken cancellationToken)
	{
		var result = await GenerateIncrementalAsync([Source], cancellationToken: cancellationToken);

		var second = StepReasons(result.Runs[1]);
		string[] frameworkStages =
		[
			"GetGenerationConfiguration",
			"GetGenerationContext_AggregateGenerationCapabilities",
		];
		await Assert
			.That(
				frameworkStages.All(stage =>
					second.TryGetValue(stage, out var reasons) && reasons.All(r => r == StepReason.Cached)
				)
			)
			.IsTrue();
	}

	[Test]
	public async Task IdenticalRerun_ProducesByteIdenticalOutput(CancellationToken cancellationToken)
	{
		var result = await GenerateIncrementalAsync([Source], cancellationToken: cancellationToken);

		await Assert.That(GeneratedSources(result.Runs[1])).IsEquivalentTo(GeneratedSources(result.Runs[0]));
	}

	[Test]
	public async Task SourceChange_MarksTargetStageModified_PropertyStagesStayCached(
		CancellationToken cancellationToken
	)
	{
		var result = await GenerateIncrementalAsync(
			[new IncrementalRunInput([Source]), new IncrementalRunInput([ChangedSource])],
			cancellationToken: cancellationToken
		);

		var second = StepReasons(result.Runs[1]);
		await Assert.That(second["GetAggregateTargets"]).Contains(StepReason.Modified);
		// The generation configuration depends only on analyzer config options, so a source-only
		// change must keep it fully cached.
		await Assert.That(second["GetGenerationConfiguration"].All(r => r == StepReason.Cached)).IsTrue();
	}

	[Test]
	public async Task PropertyChange_MarksPropertyStagesModified_TargetStageStaysCached(
		CancellationToken cancellationToken
	)
	{
		var result = await GenerateIncrementalAsync(
			[
				new IncrementalRunInput([Source]),
				new IncrementalRunInput([Source], [("build_property.DisableEventSourcingSourceGenerator", "true")]),
			],
			cancellationToken: cancellationToken
		);

		var second = StepReasons(result.Runs[1]);
		await Assert.That(second["GetGenerationConfiguration"]).Contains(StepReason.Modified);
		await Assert.That(second["GetGenerationContext_AggregateGenerationCapabilities"]).Contains(StepReason.Modified);
		// The target discovery is not invalidated by a property change: it is either cached or
		// recomputed to the identical value, but never marked Modified.
		await Assert.That(second["GetAggregateTargets"]).DoesNotContain(StepReason.Modified);
	}
}
