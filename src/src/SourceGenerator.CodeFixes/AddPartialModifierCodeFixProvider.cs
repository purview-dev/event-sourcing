using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Purview.EventSourcing.SourceGenerator.Common;

namespace Purview.EventSourcing.SourceGenerator.CodeFixes;

/// <summary>
/// Adds the missing <c>partial</c> modifier to aggregate declarations (<c>EVENTSTORE001</c>) and
/// event methods (<c>EVENTSTORE007</c>).
/// The correction is unambiguous and preserves the surrounding declaration.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddPartialModifierCodeFixProvider)), Shared]
public sealed class AddPartialModifierCodeFixProvider : CodeFixProvider
{
	const string EquivalenceKey = "AddPartialModifier";

	/// <inheritdoc/>
	public override ImmutableArray<string> FixableDiagnosticIds =>
		[DiagnosticLibrary.AggregateMustBePartial.Id, DiagnosticLibrary.EventMethodMustBePartial.Id];

	/// <inheritdoc/>
	public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	/// <inheritdoc/>
	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root is null)
			return;

		foreach (var diagnostic in context.Diagnostics)
		{
			var declaration = FindDeclaration(root, diagnostic.Location.SourceSpan.Start);
			if (declaration is null)
				continue;

			context.RegisterCodeFix(
				CodeAction.Create(
					title: "Add 'partial' modifier",
					createChangedDocument: cancellationToken =>
						AddPartialAsync(context.Document, declaration, cancellationToken),
					equivalenceKey: EquivalenceKey
				),
				diagnostic
			);
		}
	}

	static MemberDeclarationSyntax? FindDeclaration(SyntaxNode root, int position)
	{
		var token = root.FindToken(position);
		return token.Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
	}

	static async Task<Document> AddPartialAsync(
		Document document,
		MemberDeclarationSyntax declaration,
		CancellationToken cancellationToken
	)
	{
		var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
		var generator = SyntaxGenerator.GetGenerator(document);
		var updated = generator.WithModifiers(
			declaration,
			generator.GetModifiers(declaration) | DeclarationModifiers.Partial
		);
		editor.ReplaceNode(declaration, updated);
		return editor.GetChangedDocument();
	}
}
