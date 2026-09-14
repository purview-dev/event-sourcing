using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Purview.EventSourcing.SourceGenerator;

sealed class SourceGeneratorPerformanceRunner
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

	// Evidence-based regression thresholds. They are deliberately generous so they catch material
	// regressions (for example a pipeline change that regenerates every aggregate on every build)
	// without becoming flaky on fast or shared machines.
	const double MaxWarmRerunRatio = 0.80;

	const double MaxSingleAggregateEditRatio = 0.90;

	public PerformanceRun RunQuick() => Run("Quick", warmupIterations: 1, measurementIterations: 3);

	public PerformanceRun RunBenchmark() => Run("Benchmark", warmupIterations: 3, measurementIterations: 12);

	PerformanceRun Run(string mode, int warmupIterations, int measurementIterations)
	{
		var scenarios = SourceGeneratorPerformanceScenarios.All;
		List<PerformanceScenarioRun> results = new(scenarios.Count);

		foreach (var scenario in scenarios)
		{
			var baseline = Measure(
				scenario.Source,
				warmupIterations,
				measurementIterations,
				compileWithGenerator: false,
				scenario.CreateGenerator
			);
			var generator = Measure(
				scenario.Source,
				warmupIterations,
				measurementIterations,
				compileWithGenerator: true,
				scenario.CreateGenerator
			);
			var warmRerun = MeasureWarmRerun(
				scenario.Source,
				warmupIterations,
				measurementIterations,
				scenario.CreateGenerator
			);
			var singleEdit = scenario.EditedSource is null
				? 0
				: MeasureSingleAggregateEdit(
					scenario.Source,
					scenario.EditedSource,
					warmupIterations,
					measurementIterations,
					scenario.CreateGenerator
				);

			results.Add(
				new PerformanceScenarioRun
				{
					Name = scenario.Name,
					GeneratorName = scenario.GeneratorName,
					WarmupIterations = warmupIterations,
					MeasurementIterations = measurementIterations,
					BaselineAverageMilliseconds = baseline.AverageMilliseconds,
					GeneratorAverageMilliseconds = generator.AverageMilliseconds,
					WarmRerunAverageMilliseconds = warmRerun,
					SingleAggregateEditAverageMilliseconds = singleEdit,
				}
			);
		}

		ValidateThresholds(results);

		return new PerformanceRun
		{
			Mode = mode,
			TimestampUtc = DateTimeOffset.UtcNow,
			MachineName = Environment.MachineName,
			FrameworkDescription = RuntimeInformation.FrameworkDescription,
			Scenarios = results,
		};
	}

	static void ValidateThresholds(IReadOnlyList<PerformanceScenarioRun> results)
	{
		foreach (var scenario in results)
		{
			if (scenario.WarmRerunAverageMilliseconds > 0 && scenario.GeneratorAverageMilliseconds > 0)
			{
				if (scenario.WarmRerunRatio > MaxWarmRerunRatio)
				{
					throw new InvalidOperationException(
						$"[{scenario.Name}] warm rerun took {scenario.WarmRerunRatio:P0} of cold generation, exceeding the {MaxWarmRerunRatio:P0} regression threshold. The incremental pipeline is likely regenerating work that should be cached."
					);
				}
			}

			if (scenario.SingleAggregateEditAverageMilliseconds > 0 && scenario.GeneratorAverageMilliseconds > 0)
			{
				if (scenario.SingleAggregateEditRatio > MaxSingleAggregateEditRatio)
				{
					throw new InvalidOperationException(
						$"[{scenario.Name}] a single-aggregate edit took {scenario.SingleAggregateEditRatio:P0} of cold generation, exceeding the {MaxSingleAggregateEditRatio:P0} regression threshold. Editing one aggregate should only invalidate that aggregate's outputs."
					);
				}
			}
		}
	}

	static Measurement Measure(
		string source,
		int warmupIterations,
		int measurementIterations,
		bool compileWithGenerator,
		Func<IIncrementalGenerator> generatorFactory
	)
	{
		for (var i = 0; i < warmupIterations; i++)
			RunOnce(source, compileWithGenerator, generatorFactory);

		var stopwatch = Stopwatch.StartNew();
		for (var i = 0; i < measurementIterations; i++)
			RunOnce(source, compileWithGenerator, generatorFactory);
		stopwatch.Stop();

		return new Measurement(stopwatch.Elapsed.TotalMilliseconds / measurementIterations);
	}

	static void RunOnce(string source, bool compileWithGenerator, Func<IIncrementalGenerator> generatorFactory)
	{
		var syntaxTree = CSharpSyntaxTree.ParseText(source);
		var compilation = CSharpCompilation.Create(
			"SourceGeneratorPerformance",
			[syntaxTree],
			References,
			CompilationOptions
		);

		if (compileWithGenerator)
		{
			GeneratorDriver driver = CSharpGeneratorDriver.Create(generatorFactory().AsSourceGenerator());
			driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var _, out var diagnostics);

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

			return;
		}
	}

	static double MeasureWarmRerun(
		string source,
		int warmupIterations,
		int measurementIterations,
		Func<IIncrementalGenerator> generatorFactory
	)
	{
		for (var i = 0; i < warmupIterations; i++)
			WarmRerunOnce(source, generatorFactory);

		var total = 0d;
		for (var i = 0; i < measurementIterations; i++)
			total += WarmRerunOnce(source, generatorFactory);
		return total / measurementIterations;
	}

	/// <summary>
	/// Runs one cold generation followed by an incremental rerun on the same driver and compilation,
	/// returning only the rerun duration. A correct incremental pipeline makes the rerun a small
	/// fraction of the cold generation.
	/// </summary>
	static double WarmRerunOnce(string source, Func<IIncrementalGenerator> generatorFactory)
	{
		var compilation = CreateCompilation(source);
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generatorFactory().AsSourceGenerator());
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

		var stopwatch = Stopwatch.StartNew();
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
		stopwatch.Stop();

		AssertNoGeneratorExceptions(driver);
		return stopwatch.Elapsed.TotalMilliseconds;
	}

	static double MeasureSingleAggregateEdit(
		string source,
		string editedSource,
		int warmupIterations,
		int measurementIterations,
		Func<IIncrementalGenerator> generatorFactory
	)
	{
		for (var i = 0; i < warmupIterations; i++)
			SingleAggregateEditOnce(source, editedSource, generatorFactory);

		var total = 0d;
		for (var i = 0; i < measurementIterations; i++)
			total += SingleAggregateEditOnce(source, editedSource, generatorFactory);
		return total / measurementIterations;
	}

	/// <summary>
	/// Cold-generates a multi-aggregate compilation, then re-runs the driver against a compilation in
	/// which exactly one aggregate changed, returning only the edit duration. A correct incremental
	/// pipeline only regenerates the edited aggregate.
	/// </summary>
	static double SingleAggregateEditOnce(
		string source,
		string editedSource,
		Func<IIncrementalGenerator> generatorFactory
	)
	{
		var compilation = CreateCompilation(source);
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generatorFactory().AsSourceGenerator());
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

		var editedCompilation = CreateCompilation(editedSource);
		var stopwatch = Stopwatch.StartNew();
		driver = driver.RunGeneratorsAndUpdateCompilation(editedCompilation, out _, out _);
		stopwatch.Stop();

		AssertNoGeneratorExceptions(driver);
		return stopwatch.Elapsed.TotalMilliseconds;
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

	sealed record Measurement(double AverageMilliseconds);
}
