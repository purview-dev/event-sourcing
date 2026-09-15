namespace Purview.EventSourcing.SourceGenerator.Generators;

[Generator]
public sealed partial class AggregateSourceGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context
			.RegisterEmbeddedAttribute<AggregateSourceGenerator>()
			.RegisterPostInitializationOutput(ctx =>
			{
				foreach (var (HintName, Source) in AggregateAttributeEmitter.Emit())
					ctx.AddSource(HintName, Source);
			});

		var generationContext = SourceGenLibrary.GetGenerationContext(context);

		// Keep the compilation reference stable across identical reruns so the incremental pipeline
		// short-circuits instead of re-executing every aggregate transform (see PreCompilationMarker).
#pragma warning disable RSEXPERIMENTAL007 // Pre-compilation source output is intentionally used to stabilize the incremental cache.
		context.RegisterPreCompilationSourceOutput(
			Common.PreCompilationMarker.Provider(context),
			static (spc, source) => spc.AddSource(Common.PreCompilationMarker.HintName, source)
		);
#pragma warning restore RSEXPERIMENTAL007

		var aggregateTargets = SourceGenLibrary.GetAggregateTargets(context);

		context.RegisterSourceOutput(
			aggregateTargets.Combine(generationContext),
			static (spc, tuple) =>
			{
				var (aggregateResult, context) = tuple;
				if (context.Settings.IsSourceGeneratorDisabled)
					return;

				if (!aggregateResult.ShouldProcess || !aggregateResult.HasValue)
					return;

				var info = aggregateResult.Value.Info;
				var writer = context.CreateCodeWriter();

				AggregateEmitContext outputContext = new(context, info);
				AggregateSourceEmitter.GenerateAggregateSource(outputContext, writer);

				spc.AddSource(info.HintName, writer);
			}
		);

		var manifest = SourceGenLibrary.GetEventContractManifest(aggregateTargets);
		var locations = SourceGenLibrary.GetEventContractLocations(aggregateTargets);
		var baseline = SourceGenLibrary.GetEventContractBaseline(context);
		var manifestEnabled = SourceGenLibrary.IsEventContractManifestEnabled(context);
		var comparison = SourceGenLibrary.GetEventContractComparison(manifest, baseline);

		context.RegisterSourceOutput(
			comparison.Combine(locations).Combine(generationContext),
			static (spc, tuple) =>
			{
				var ((comparison, locations), generationContext) = tuple;
				if (generationContext.Settings.IsSourceGeneratorDisabled)
					return;

				foreach (var issue in comparison.Issues)
					spc.ReportDiagnostic(EventContractDiagnostics.CreateDiagnostic(issue, locations));
			}
		);

		context.RegisterSourceOutput(
			manifest.Combine(manifestEnabled).Combine(generationContext),
			static (spc, tuple) =>
			{
				var ((manifest, enabled), generationContext) = tuple;
				if (generationContext.Settings.IsSourceGeneratorDisabled)
					return;

				if (!enabled)
					return;

				spc.AddSource(
					EventContractManifestLibrary.GeneratedHintName,
					EventContractManifestLibrary.EmitSource(manifest)
				);
			}
		);
	}
}
