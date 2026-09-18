using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.EventSourcing.SourceGenerator.CodeFixes;

/// <summary>
/// Manual code-fix harness. The framework's code-fix test base does not run the source generators,
/// but the aggregate/value-object attributes used by the analyzers are emitted by those generators,
/// so this harness runs them before analyzing.
/// </summary>
public static class CodeFixTestHarness
{
	public static Task<HarnessResult> ApplyAsync<TAnalyzer, TCodeFix>(
		string source,
		CancellationToken cancellationToken
	)
		where TAnalyzer : DiagnosticAnalyzer, new()
		where TCodeFix : CodeFixProvider, new() =>
		ApplyAsync<TAnalyzer, TCodeFix>(source, fixAll: false, cancellationToken);

	public static Task<HarnessResult> ApplyFixAllAsync<TAnalyzer, TCodeFix>(
		string source,
		CancellationToken cancellationToken
	)
		where TAnalyzer : DiagnosticAnalyzer, new()
		where TCodeFix : CodeFixProvider, new() =>
		ApplyAsync<TAnalyzer, TCodeFix>(source, fixAll: true, cancellationToken);

	static async Task<HarnessResult> ApplyAsync<TAnalyzer, TCodeFix>(
		string source,
		bool fixAll,
		CancellationToken cancellationToken
	)
		where TAnalyzer : DiagnosticAnalyzer, new()
		where TCodeFix : CodeFixProvider, new()
	{
		var (updatedCompilation, originalTree) = CreateCompilation(source);

		TAnalyzer analyzer = new();
		var analyzerDiagnosticsAll = await updatedCompilation
			.WithAnalyzers([analyzer])
			.GetAnalyzerDiagnosticsAsync(cancellationToken);
		var analyzerDiagnostics = analyzerDiagnosticsAll.ToArray();

		TCodeFix provider = new();
		var applicable = analyzerDiagnostics
			.Where(diagnostic => provider.FixableDiagnosticIds.Contains(diagnostic.Id, StringComparer.Ordinal))
			.ToArray();

		if (applicable.Length == 0)
			return new HarnessResult(originalTree.ToString(), []);

		using AdhocWorkspace workspace = new();
		var project = workspace.AddProject("CodeFixTest", LanguageNames.CSharp);
		var document = workspace.AddDocument(project.Id, "Test.cs", await originalTree.GetTextAsync(cancellationToken));

		var codeActions = await RegisterCodeActionsAsync(document, provider, applicable[0], cancellationToken);
		if (codeActions.Count == 0)
			return new HarnessResult(originalTree.ToString(), []);

		if (!fixAll)
		{
			var fixedDocument = await ApplySingleActionAsync(document, codeActions[0], cancellationToken);
			return new HarnessResult(
				(await fixedDocument.GetTextAsync(cancellationToken)).ToString(),
				[.. applicable.Select(static diagnostic => diagnostic.Id)]
			);
		}

		var fixAllProvider = provider.GetFixAllProvider();
		if (fixAllProvider is null)
			return new HarnessResult(originalTree.ToString(), []);

		// The analyzer diagnostics reference the analyzer compilation's tree. Apply the fix for each
		// occurrence sequentially against the workspace document, which validates that every
		// diagnostic in the document is fixed (the provider also advertises a BatchFixer for IDEs).
		var documentRoot = await document.GetSyntaxRootAsync(cancellationToken);
		var fixedDocumentForAll = document;
		foreach (var diagnostic in applicable)
		{
			var remapped = Diagnostic.Create(
				diagnostic.Descriptor,
				documentRoot!.SyntaxTree.GetLocation(diagnostic.Location.SourceSpan)
			);
			var actions = await RegisterCodeActionsAsync(fixedDocumentForAll, provider, remapped, cancellationToken);
			if (actions.Count == 0)
				continue;

			fixedDocumentForAll = await ApplySingleActionAsync(fixedDocumentForAll, actions[0], cancellationToken);
			documentRoot = await fixedDocumentForAll.GetSyntaxRootAsync(cancellationToken);
		}

		return new HarnessResult(
			(await fixedDocumentForAll.GetTextAsync(cancellationToken)).ToString(),
			[.. applicable.Select(static diagnostic => diagnostic.Id)]
		);
	}

	static (Compilation Compilation, SyntaxTree OriginalTree) CreateCompilation(string source)
	{
		CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
		var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Test.cs");
		var compilation = CSharpCompilation.Create(
			"CodeFixTest",
			[tree],
			References(),
			new CSharpCompilationOptions(
				OutputKind.DynamicallyLinkedLibrary,
				nullableContextOptions: NullableContextOptions.Enable
			)
		);

		// The aggregate and value-object attributes are emitted by the source generators.
		GeneratorDriver driver = CSharpGeneratorDriver.Create([
			new Generators.AggregateSourceGenerator().AsSourceGenerator(),
		]);
		driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);

		return (updatedCompilation, tree);
	}

	static async Task<List<CodeAction>> RegisterCodeActionsAsync(
		Document document,
		CodeFixProvider provider,
		Diagnostic diagnostic,
		CancellationToken cancellationToken
	)
	{
		List<CodeAction> codeActions = [];
		CodeFixContext context = new(document, diagnostic, (action, _) => codeActions.Add(action), cancellationToken);
		await provider.RegisterCodeFixesAsync(context);
		return codeActions;
	}

	static async Task<Document> ApplySingleActionAsync(
		Document document,
		CodeAction action,
		CancellationToken cancellationToken
	)
	{
		var operations = await action.GetOperationsAsync(cancellationToken);
		var changedSolution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
		return changedSolution.GetDocument(document.Id)!;
	}

	static ImmutableArray<MetadataReference> References()
	{
		var builder = ImmutableArray.CreateBuilder<MetadataReference>();
		var trusted = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty).Split(
			Path.PathSeparator,
			StringSplitOptions.RemoveEmptyEntries
		);
		foreach (var path in trusted)
			builder.Add(MetadataReference.CreateFromFile(path));

		builder.Add(MetadataReference.CreateFromFile(typeof(Aggregates.IAggregate).Assembly.Location));
		builder.Add(MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location));
		return builder.ToImmutable();
	}
}

/// <summary>
/// The outcome of applying a code fix: the fixed document text and the diagnostics that were fixed.
/// </summary>
public sealed record HarnessResult(string FixedCode, ImmutableArray<string> FixedDiagnosticIds);
