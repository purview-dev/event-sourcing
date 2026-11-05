using System.Collections.Immutable;
using StepReason = Microsoft.CodeAnalysis.IncrementalStepRunReason;

namespace Purview.EventSourcing.SourceGenerator.Incremental;

/// <summary>
/// Incremental caching guarantees for the event-contract manifest and baseline comparison:
/// identical reruns stay cached, trivia-only changes do not recompare contracts, and baseline
/// content changes only affect the comparison (never aggregate generation).
/// </summary>
public sealed class EventContractManifestIncrementalTests : AggregateSourceGeneratorTestBase
{
	const string OrderSource = """
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

	const string OrderSourceWithLeadingComment = """
		// Semantically irrelevant comment.
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

	const string BaselineV1Json = /*lang=json,strict*/
		"""
		{"formatVersion":1,"aggregates":[{"name":"OrderAggregate","namespace":"Testing","events":[{"name":"OrderCreatedEvent","namespace":"Testing.OrderEvents","method":"CreateOrder","schemaVersion":1,"fields":[{"name":"CustomerId","type":"string","elementType":null,"isArray":false,"isNullable":false,"isRequired":false,"isString":true}]}]}]}
		""";

	const string BaselineV2Json = /*lang=json,strict*/
		"""
		{"formatVersion":1,"aggregates":[{"name":"OrderAggregate","namespace":"Testing","events":[{"name":"OrderCreatedEvent","namespace":"Testing.OrderEvents","method":"CreateOrder","schemaVersion":2,"fields":[{"name":"CustomerId","type":"string","elementType":null,"isArray":false,"isNullable":false,"isRequired":false,"isString":true}]}]}]}
		""";

	static EventSourcingGeneratorTestOptions WithBaseline(string json) =>
		new() { AdditionalText = [new InMemoryAdditionalText("EventContractManifest.json", json)] };

	[Test]
	public async Task IdenticalRerun_ManifestStagesCached(CancellationToken cancellationToken)
	{
		var result = await GenerateIncrementalAsync([OrderSource], WithBaseline(BaselineV1Json), cancellationToken);

		var second = StepReasons(result.Runs[1]);
		foreach (var stepName in new[] { "EventContractManifest", "EventContractBaseline", "EventContractComparison" })
		{
			await Assert.That(second.TryGetValue(stepName, out var reasons)).IsTrue();
			await Assert.That(reasons.All(reason => reason is StepReason.Cached or StepReason.Unchanged)).IsTrue();
		}
	}

	[Test]
	public async Task TriviaOnlyChange_DoesNotRecompareContracts(CancellationToken cancellationToken)
	{
		var result = await GenerateIncrementalAsync(
			[new IncrementalRunInput([OrderSource]), new IncrementalRunInput([OrderSourceWithLeadingComment])],
			WithBaseline(BaselineV1Json),
			cancellationToken
		);

		var second = StepReasons(result.Runs[1]);

		await Assert.That(second["EventContractBaseline"]).DoesNotContain(StepReason.Modified);
		await Assert.That(second["EventContractManifest"]).DoesNotContain(StepReason.Modified);
		await Assert.That(second["EventContractComparison"]).DoesNotContain(StepReason.Modified);
	}

	[Test]
	public async Task BaselineContentChange_AffectsOnlyComparisonDiagnostics(CancellationToken cancellationToken)
	{
		using var compatible = await GenerateAsync(OrderSource, WithBaseline(BaselineV1Json), cancellationToken);
		await Assert.That(compatible).DoesNotHaveDiagnostic("EVENTSTORE035");

		using var regressed = await GenerateAsync(OrderSource, WithBaseline(BaselineV2Json), cancellationToken);
		await Assert.That(regressed).HasDiagnostic("EVENTSTORE035");

		var compatibleGenerated = GeneratedAggregateSources(compatible);
		var regressedGenerated = GeneratedAggregateSources(regressed);
		await Assert.That(regressedGenerated).IsEquivalentTo(compatibleGenerated);
	}

	static ImmutableArray<string> GeneratedAggregateSources(DriverRunResult result) =>
		[
			.. result
				.DriverResult.Results[0]
				.GeneratedSources.Where(static source => source.HintName.EndsWith(".g.cs", StringComparison.Ordinal))
				.Where(static source => source.HintName != EventContractManifestLibrary.GeneratedHintName)
				.Select(static source => source.SourceText.ToString()),
		];

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
}
