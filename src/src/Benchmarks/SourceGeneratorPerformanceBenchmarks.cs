using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Purview.EventSourcing.SourceGenerator;

namespace Purview.EventSourcing.Benchmarks;

/// <summary>
/// Measures how fast the aggregate and value-object generators run and, crucially, how well the
/// incremental pipeline caches: <see cref="ColdGeneration"/> is a fresh compile, while
/// <see cref="WarmRerun"/> and <see cref="SingleAggregateEdit"/> re-run the same driver after an
/// incremental change. The exporter turns the means into ratios and enforces the regression
/// thresholds.
/// </summary>
public class SourceGeneratorPerformanceBenchmarks
{
	static readonly MetadataReference[] References =
	[
		MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
		MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
		MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location),
		MetadataReference.CreateFromFile(
			System
				.Reflection.Assembly.Load(
					"netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51"
				)
				.Location
		),
	];

	static readonly CSharpCompilationOptions CompilationOptions = new(OutputKind.DynamicallyLinkedLibrary);

	public static IEnumerable<SourceGeneratorScenario> AllScenarios => SourceGeneratorPerformanceScenarios.All;

	[ParamsSource(nameof(AllScenarios))]
	public SourceGeneratorScenario Scenario { get; set; } = null!;

	CSharpCompilation _compilation = null!;

	CSharpCompilation _editedCompilation = null!;

	GeneratorDriver _driver = null!;

	[IterationSetup]
	public void SetupIncremental()
	{
		ArgumentNullException.ThrowIfNull(Scenario);
		_compilation = CreateCompilation(Scenario.Source);
		_driver = CSharpGeneratorDriver.Create(Scenario.CreateGenerator().AsSourceGenerator());
		RunAndAssert(_driver, _compilation);
		_editedCompilation = CreateCompilation(Scenario.EditedSource ?? Scenario.Source);
	}

	[Benchmark]
	public void ColdGeneration()
	{
		ArgumentNullException.ThrowIfNull(Scenario);
		var compilation = CreateCompilation(Scenario.Source);
		var driver = CSharpGeneratorDriver.Create(Scenario.CreateGenerator().AsSourceGenerator());
		RunAndAssert(driver, compilation);
	}

	[Benchmark]
	public void WarmRerun()
	{
		_driver = _driver.RunGeneratorsAndUpdateCompilation(_compilation, out _, out _);
		AssertNoGeneratorExceptions(_driver);
	}

	[Benchmark]
	public void SingleAggregateEdit()
	{
		_driver = _driver.RunGeneratorsAndUpdateCompilation(_editedCompilation, out _, out _);
		AssertNoGeneratorExceptions(_driver);
	}

	static void RunAndAssert(GeneratorDriver driver, Compilation compilation)
	{
		driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

		foreach (var diagnostic in diagnostics)
		{
			if (diagnostic.Severity == DiagnosticSeverity.Error)
				throw new InvalidOperationException(diagnostic.ToString());
		}

		foreach (var generatorResult in driver.GetRunResult().Results)
		{
			if (generatorResult.Exception is not null)
				throw generatorResult.Exception;
		}
	}

	static void AssertNoGeneratorExceptions(GeneratorDriver driver)
	{
		foreach (var generatorResult in driver.GetRunResult().Results)
		{
			if (generatorResult.Exception is not null)
				throw generatorResult.Exception;
		}
	}

	static CSharpCompilation CreateCompilation(string source)
	{
		var syntaxTree = CSharpSyntaxTree.ParseText(source);
		return CSharpCompilation.Create("SourceGeneratorPerformance", [syntaxTree], References, CompilationOptions);
	}
}
