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
/// Adds the missing <c>[EventContract]</c> attribute to a hand-written event type referenced by
/// <c>Register&lt;TEvent&gt;</c>/<c>RegisterGenerated&lt;TEvent&gt;</c> (<c>EVENTSTORE037</c>), so the
/// framework can recognise it as an event contract.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddEventContractCodeFixProvider)), Shared]
public sealed class AddEventContractCodeFixProvider : CodeFixProvider
{
	const string EquivalenceKey = "AddEventContract";

	/// <inheritdoc/>
	public override ImmutableArray<string> FixableDiagnosticIds =>
		[DiagnosticLibrary.EventTypeShouldBeMarkedWithEventContract.Id];

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
					title: "Mark as [EventContract]",
					createChangedDocument: cancellationToken =>
						AddEventContractAsync(context.Document, declaration, cancellationToken),
					equivalenceKey: EquivalenceKey
				),
				diagnostic
			);
		}
	}

	static TypeDeclarationSyntax? FindDeclaration(SyntaxNode root, int position)
	{
		var token = root.FindToken(position);
		return token.Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
	}

	static async Task<Document> AddEventContractAsync(
		Document document,
		TypeDeclarationSyntax declaration,
		CancellationToken cancellationToken
	)
	{
		var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
		var generator = SyntaxGenerator.GetGenerator(document);

		var attribute = generator.Attribute("global::Purview.EventSourcing.Aggregates.Events.EventContract");
		var updated = generator.AddAttributes(declaration, attribute);
		editor.ReplaceNode(declaration, updated);

		return editor.GetChangedDocument();
	}
}
