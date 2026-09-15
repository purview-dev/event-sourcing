using Microsoft.CodeAnalysis.Text;

namespace Purview.EventSourcing.SourceGenerator.Common;

/// <summary>
/// Emits a single inert pre-compilation source file. Registering a pre-compilation source output
/// makes Roslyn's <c>CompilationCache</c> reuse the previous run's compilation reference on an
/// identical rerun (instead of regenerating it because of the post-initialization attribute trees),
/// which in turn lets <c>SyntaxProvider.ForAttributeWithMetadataName</c> short-circuit and skip
/// re-executing the per-candidate transforms.
/// </summary>
static class PreCompilationMarker
{
	public const string HintName = "PreCompilationMarker.g.cs";

	/// <summary>
	/// The marker text is a static instance so its <see cref="SourceText"/> reference is stable
	/// across incremental reruns within a process, which is what the compilation-cache key requires.
	/// </summary>
	public static readonly SourceText Source = SourceText.From(
		"// Purview.EventSourcing pre-compilation marker.",
		System.Text.Encoding.UTF8
	);

	public static IncrementalValueProvider<SourceText> Provider(IncrementalGeneratorInitializationContext context) =>
		context.AnalyzerConfigOptionsProvider.Select(static (_, _) => Source);
}
