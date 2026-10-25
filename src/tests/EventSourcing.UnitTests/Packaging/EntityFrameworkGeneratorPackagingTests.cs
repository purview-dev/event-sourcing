using System.Xml.Linq;

namespace Purview.EventSourcing.Packaging;

/// <summary>
/// Guards the rule that the Entity Framework Core source generator is packaged by exactly one project.
/// </summary>
/// <remarks>
/// Roslyn loads an analyzer per path, not per identity. When three provider packages each carried a copy,
/// a consumer referencing two of them ran the generator twice and the second copy of its post-initialization
/// output failed the build with CS0579 - reported against a path in the consumer's own obj directory, naming
/// none of the packages responsible. NuGet dedupes by package identity, so the generator now ships only with
/// Purview.EventSourcing.EntityFrameworkCore, which every Entity Framework provider depends on. The providers
/// keep the reference for their own compilation and opt out of bundling it with Pack="false".
/// </remarks>
public class EntityFrameworkGeneratorPackagingTests
{
	const string GeneratorProject = "EntityFrameworkCore.SourceGenerator.csproj";

	static DirectoryInfo RepositorySource()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src", "EventSourcing.slnx")))
			directory = directory.Parent;

		return directory == null
			? throw new DirectoryNotFoundException(
				"Could not locate the repository root from the test output directory."
			)
			: new DirectoryInfo(Path.Combine(directory.FullName, "src", "src"));
	}

	static XElement[] GeneratorReferences(string projectFile) =>
		[
			.. XDocument
				.Load(projectFile)
				.Descendants("ProjectReference")
				.Where(reference =>
					reference.Attribute("Include")?.Value.EndsWith(GeneratorProject, StringComparison.Ordinal) == true
				),
		];

	[Test]
	public async Task EntityFrameworkCoreProject_IsPackableAndReferencesTheGenerator()
	{
		var project = Path.Combine(RepositorySource().FullName, "EntityFrameworkCore", "EntityFrameworkCore.csproj");
		var document = XDocument.Load(project);

		await Assert.That(document.Descendants("IsPackable").Select(x => x.Value)).Contains("true");
		await Assert.That(GeneratorReferences(project)).Count().IsEqualTo(1);
	}

	[Test]
	[Arguments("SqlServer")]
	[Arguments("Postgres")]
	[Arguments("CosmosDb")]
	public async Task EntityFrameworkProvider_ReferencesTheGenerator_WithoutPackagingIt(string provider)
	{
		var project = Path.Combine(RepositorySource().FullName, provider, $"{provider}.csproj");
		var references = GeneratorReferences(project);

		await Assert.That(references).Count().IsEqualTo(1);
		await Assert.That(references[0].Attribute("Pack")?.Value).IsEqualTo("false");
	}
}
