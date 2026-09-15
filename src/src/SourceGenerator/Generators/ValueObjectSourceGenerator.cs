using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Purview.EventSourcing.SourceGenerator.Generators;

[Generator]
public sealed partial class ValueObjectSourceGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context
			.RegisterEmbeddedAttribute<ValueObjectSourceGenerator>()
			.RegisterPostInitializationOutput(ctx =>
			{
				foreach (var (HintName, Source) in ValueObjectsAttributeEmitter.EmitAttributes())
					ctx.AddSource(HintName, Source);
			});

		var generationContext = IncrementalPipeline.GenerationContextValueProvider(
			context,
			SourceGenLibrary.CreateGenerationSettings<ValueObjectSourceGenerator>(
				PropertyLibrary.DisableSourceGenerator
			),
			static (_, _, _, _) => EmptyCapabilities.Instance
		);

		// Keep the compilation reference stable across identical reruns so the incremental pipeline
		// short-circuits instead of re-executing every value-object transform (see PreCompilationMarker).
#pragma warning disable RSEXPERIMENTAL007 // Pre-compilation source output is intentionally used to stabilize the incremental cache.
		context.RegisterPreCompilationSourceOutput(
			Common.PreCompilationMarker.Provider(context),
			static (spc, source) => spc.AddSource(Common.PreCompilationMarker.HintName, source)
		);
#pragma warning restore RSEXPERIMENTAL007

		var scalarCandidates = context
			.SyntaxProvider.ForAttributeWithMetadataName(
				ValueObjectSymbolInspector.ScalarAttributeName,
				predicate: static (node, _) => node is TypeDeclarationSyntax,
				transform: static (ctx, ct) =>
					ScalarValueObjectModelBuilder.Build(
						(INamedTypeSymbol)ctx.TargetSymbol,
						(TypeDeclarationSyntax)ctx.TargetNode,
						ct
					)
			)
			.WithTrackingName("GetScalarValueObjectTargets");

		var complexCandidates = context
			.SyntaxProvider.ForAttributeWithMetadataName(
				ValueObjectSymbolInspector.ValueObjectAttributeName,
				predicate: static (node, _) => node is TypeDeclarationSyntax,
				transform: static (ctx, ct) =>
					ComplexValueObjectModelBuilder.Build(
						(INamedTypeSymbol)ctx.TargetSymbol,
						(TypeDeclarationSyntax)ctx.TargetNode,
						ctx.SemanticModel.Compilation,
						ct
					)
			)
			.WithTrackingName("GetComplexValueObjectTargets");

		context.RegisterSourceOutput(
			scalarCandidates.Combine(generationContext),
			static (spc, tuple) => EmitScalarResult(spc, tuple.Left, tuple.Right)
		);
		context.RegisterSourceOutput(
			complexCandidates.Combine(generationContext),
			static (spc, tuple) => EmitComplexResult(spc, tuple.Left, tuple.Right)
		);
	}

	static void EmitScalarResult(
		SourceProductionContext context,
		GeneratorResult<ScalarValueObjectModel> result,
		GeneratorContext generationContext
	)
	{
		if (generationContext.Settings.IsSourceGeneratorDisabled)
			return;

		if (!result.ShouldProcess)
			return;

		var writer = generationContext.CreateCodeWriter();
		ScalarValueObjectEmitter.Emit(writer, result.Value);
		context.AddSource(result.Value.HintName, writer);
	}

	static void EmitComplexResult(
		SourceProductionContext context,
		GeneratorResult<ComplexValueObjectModel> result,
		GeneratorContext generationContext
	)
	{
		if (generationContext.Settings.IsSourceGeneratorDisabled)
			return;

		if (!result.ShouldProcess)
			return;

		var writer = generationContext.CreateCodeWriter();
		ComplexValueObjectEmitter.Emit(writer, result.Value);
		context.AddSource(result.Value.HintName, writer);
	}
}
