using Microsoft.CodeAnalysis;
using Purview.EventSourcing.EntityFrameworkCore.SourceGenerator.Emitters;

namespace Purview.EventSourcing.EntityFrameworkCore.SourceGenerator;

[Generator]
public sealed class EFSourceGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context
			.RegisterEmbeddedAttribute<EFSourceGenerator>()
			.RegisterPostInitializationOutput(static output =>
			{
				foreach (var (hintName, source) in AttributeEmitter.GetAttributeSources())
					output.AddSource(hintName, source);
			});
	}
}
