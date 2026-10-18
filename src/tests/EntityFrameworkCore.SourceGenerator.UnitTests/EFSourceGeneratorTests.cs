using Purview.EventSourcing.EntityFrameworkCore.SourceGenerator.Heleprs;

namespace Purview.EventSourcing.EntityFrameworkCore.SourceGenerator;

public sealed class EfSourceGeneratorTests
	: TUnitSourceGeneratorTestBase<EFSourceGenerator, EFSourceGeneratorTestOptions>
{
	[Test]
	public async Task Generate_EmitsEFOpaqueAttribute(CancellationToken cancellationToken)
	{
		const string source = "namespace Testing { public sealed class Model { } }";

		using var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasNoDiagnostics();
		await Assert.That(result).HasSymbol(TypeLibrary.EFOpaqueAttribute);
	}
}
