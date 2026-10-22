using Microsoft.CodeAnalysis.CSharp.Syntax;
using Purview.EventSourcing.SourceGenerator.Generators;

namespace Purview.EventSourcing.SourceGenerator.Common;

static partial class SourceGenLibrary
{
	public static GenerationSettings CreateGenerationSettings<TGenerator>(string? disablePropertyName = null) =>
		GenerationSettings.Create<TGenerator>(disablePropertyName) with
		{
			DefaultMethodAccessibility = null,
		};

	public static IncrementalValueProvider<GenerationContext<AggregateGenerationCapabilities>> GetGenerationContext(
		IncrementalGeneratorInitializationContext context
	) =>
		IncrementalPipeline.GenerationContextValueProvider<AggregateGenerationCapabilities>(
			context,
			CreateGenerationSettings<AggregateSourceGenerator>(PropertyLibrary.DisableSourceGenerator),
			static (compilation, settings, logger, _) =>
			{
				var hasAggregateBase =
					compilation.GetTypeByMetadataName(
						TypeLibrary.Purview.EventSourcing.Aggregates.AggregateBase.MetadataFullName
					)
					is not null;
				return new(hasAggregateBase);
			}
		);

	public static IncrementalValuesProvider<GeneratorResult<AggregateTarget>> GetAggregateTargets(
		IncrementalGeneratorInitializationContext context
	) =>
		IncrementalPipeline.ForAttributeWithMetadataName(
			context,
			TypeLibrary.Purview.EventSourcing.Aggregates.AggregateAttribute,
			predicate: static (s, _) => s is ClassDeclarationSyntax,
			transform: static (ctx, ct) =>
			{
				var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
				var syntax = (ClassDeclarationSyntax)ctx.TargetNode;
				var info = AggregateInfoBuilder.Build(classSymbol, syntax, ctx.SemanticModel.Compilation, ct);

				if (!info.ShouldProcess || !info.HasValue)
					return GeneratorResult<AggregateTarget>.Create([.. info.Diagnostics]);

				AggregateTarget target = new(
					info.Value,
					EventContractBuilder.Build(info.Value),
					EventContractBuilder.BuildLocations(info.Value, classSymbol, syntax)
				);
				return GeneratorResult<AggregateTarget>.Create(target, info.Diagnostics);
			},
			trackingName: "GetAggregateTargets"
		);

	public static IncrementalValueProvider<EventContractManifest> GetEventContractManifest(
		IncrementalValuesProvider<GeneratorResult<AggregateTarget>> targets
	) =>
		targets
			.Where(static result => result.ShouldProcess && result.HasValue)
			.Select(static (result, _) => result.Value.Contract)
			.Collect()
			.Select(static (contracts, _) => EventContractManifestLibrary.BuildManifest(new(contracts)))
			.WithTrackingName("EventContractManifest");

	public static IncrementalValueProvider<EquatableArray<AggregateContractLocations>> GetEventContractLocations(
		IncrementalValuesProvider<GeneratorResult<AggregateTarget>> targets
	) =>
		targets
			.Where(static result => result.ShouldProcess && result.HasValue)
			.Select(static (result, _) => result.Value.Locations)
			.Collect()
			.Select(static (locations, _) => new EquatableArray<AggregateContractLocations>(locations))
			.WithTrackingName("EventContractLocations");

	public static IncrementalValueProvider<EventContractComparison> GetEventContractComparison(
		IncrementalValueProvider<EventContractManifest> manifest,
		IncrementalValueProvider<BaselineState> baseline
	) =>
		manifest
			.Combine(baseline)
			.Select(static (pair, _) => EventContractComparer.Compare(pair.Left, pair.Right))
			.WithTrackingName("EventContractComparison");

	public static IncrementalValueProvider<BaselineState> GetEventContractBaseline(
		IncrementalGeneratorInitializationContext context
	)
	{
		var fileName = IncrementalPipeline.PropertyValueProvider(
			context,
			EventContractManifestLibrary.BaselineFileNameProperty,
			static value =>
				string.IsNullOrWhiteSpace(value) ? EventContractManifestLibrary.BaselineFileName : value!.Trim()
		);

		return context
			.AdditionalTextsProvider.Combine(fileName)
			.Where(pair => IsBaselineFile(pair.Left?.Path ?? string.Empty, pair.Right))
			.Collect()
			.Select(
				static (pairs, ct) =>
				{
					ct.ThrowIfCancellationRequested();
					if (pairs.IsEmpty)
						return new BaselineState(null, null);

					var (left, right) = pairs[0];
					var content = left.GetText(ct)?.ToString() ?? string.Empty;
					return EventContractManifestLibrary.Parse(content, right);
				}
			)
			.WithTrackingName("EventContractBaseline");
	}

	public static IncrementalValueProvider<bool> IsEventContractManifestEnabled(
		IncrementalGeneratorInitializationContext context
	) =>
		IncrementalPipeline.PropertyValueProvider(
			context,
			EventContractManifestLibrary.ManifestEnabledProperty,
			static value => bool.TryParse(value, out var enabled) && enabled
		);

	static bool IsBaselineFile(string path, string fileName)
	{
		var normalized = path.Replace('\\', '/');
		var lastSlash = normalized.LastIndexOf('/');
		var actualName = lastSlash >= 0 ? normalized.Substring(lastSlash + 1) : normalized;
		return StringComparer.Ordinal.Equals(actualName, fileName);
	}
}
