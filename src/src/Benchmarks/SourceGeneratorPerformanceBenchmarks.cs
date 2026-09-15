using System.Collections.Concurrent;
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

	static readonly GeneratorDriverOptions DriverOptions = new(
		IncrementalGeneratorOutputKind.None,
		trackIncrementalGeneratorSteps: true
	);

	public static IEnumerable<SourceGeneratorScenario> AllScenarios => SourceGeneratorPerformanceScenarios.All;

	/// <summary>
	/// Captured incremental step run-reasons per phase+scenario (diagnostic only). Populated on the
	/// first invocation of each benchmark so the warm-rerun caching behaviour can be inspected.
	/// </summary>
	public static readonly ConcurrentDictionary<string, string> StepReasonsByCase = new();

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
		_driver = CSharpGeneratorDriver.Create(
			[Scenario.CreateGenerator().AsSourceGenerator()],
			driverOptions: DriverOptions
		);
		_driver = _driver.RunGeneratorsAndUpdateCompilation(_compilation, out _, out _);
		AssertNoGeneratorExceptions(_driver);
		_editedCompilation = CreateCompilation(Scenario.EditedSource ?? Scenario.Source);
	}

	[Benchmark]
	public void ColdGeneration()
	{
		ArgumentNullException.ThrowIfNull(Scenario);
		var compilation = CreateCompilation(Scenario.Source);
		var driver = CSharpGeneratorDriver.Create(
			[Scenario.CreateGenerator().AsSourceGenerator()],
			driverOptions: DriverOptions
		);
		RunAndAssert(driver, compilation);
		CaptureStepReasons(driver, "ColdGeneration");
	}

	[Benchmark]
	public void WarmRerun()
	{
		_driver = _driver.RunGeneratorsAndUpdateCompilation(_compilation, out _, out _);
		AssertNoGeneratorExceptions(_driver);
		CaptureStepReasons(_driver, "WarmRerun");
	}

	[Benchmark]
	public void SingleAggregateEdit()
	{
		_driver = _driver.RunGeneratorsAndUpdateCompilation(_editedCompilation, out _, out _);
		AssertNoGeneratorExceptions(_driver);
		CaptureStepReasons(_driver, "SingleAggregateEdit");
	}

	void CaptureStepReasons(GeneratorDriver driver, string phase)
	{
		var key = $"{phase}|{Scenario.Name}";
		if (!StepReasonsByCase.TryAdd(key, string.Empty))
			return;

		var results = driver.GetRunResult().Results;
		if (results.IsDefaultOrEmpty)
			return;

		var generatorResult = results[0];
		if (generatorResult.TrackedSteps is null)
			return;

		var reasons = generatorResult
			.TrackedSteps.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
			.Select(static pair =>
				$"{pair.Key}=[{string.Join(",", pair.Value.SelectMany(static s => s.Outputs).Select(static o => o.Reason))}]"
			);

		StepReasonsByCase[key] = string.Join(" ", reasons);
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
