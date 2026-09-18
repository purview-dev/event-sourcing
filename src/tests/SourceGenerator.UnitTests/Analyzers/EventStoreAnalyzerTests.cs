using Purview.SourceGeneratorFramework;

namespace Purview.EventSourcing.SourceGenerator.Analyzers;

public sealed class EventStoreAnalyzerTests : AnalyzerTestBase<EventStoreAnalyzer>
{
	[Test]
	public async Task Generate_GivenComputedParameterIsExplicitlyPassed_ReportsDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing;

public enum ReportProcessingStatus
{
	Uploaded,
	Complete,
	Failed
}

[Aggregate]
public partial class ReportUploadAggregate
{
	public string Blob { get; private set; }

	public object Summary { get; private set; }

	public ReportProcessingStatus Status { get; private set; }

	[Event]
	public partial ReportUploadAggregate MarkAsCompleted(
		string blob,
		object summary,
		[Computed] ReportProcessingStatus status = default
	);
}

public static class Caller
{
	public static void Run(ReportUploadAggregate aggregate)
	{
		aggregate.MarkAsCompleted(""blob://1"", new object(), ReportProcessingStatus.Failed);
	}
}
";

		var result = await AnalyzeAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.ComputedParameterCannotBeSetByCaller);
	}

	[Test]
	public async Task Generate_GivenEventMethodOutsideAggregate_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source =
			@"
namespace Testing;

public class UtilityType
{
	[Event]
	public void DoWork(string value) { }
}
";

		var result = await AnalyzeAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.EventMethodRequiresAggregateAttribute);
	}

	[Test]
	public async Task Generate_GivenNullableScalarComparedToNullWithEquality_ReportsPatternMatchingWarning(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing;

[Scalar]
public readonly partial record struct ProjectId
{
	public string Value { get; }

	private ProjectId(string value) => Value = value;
}

[Aggregate]
public partial class ReportAggregate
{
	public string Name { get; private set; } = string.Empty;

	[Event]
	public partial void SetName(string name);

	public bool ShouldClear(ProjectId? projectId) => projectId == null;
}
";

		var result = await AnalyzeAsync(source, cancellationToken);

		await Assert
			.That(result)
			.HasDiagnostic(DiagnosticLibrary.NullableScalarEqualityNullComparisonShouldUsePatternMatching);
	}

	protected override AnalyzerTestOptions OnBeforeRun(
		IEnumerable<string> sources,
		AnalyzerTestOptions options,
		CancellationToken cancellationToken
	)
	{
		return base.OnBeforeRun(
			sources,
			options
				// We include the emitted attributes otherwise the matching doesn't work.
				// This also means we don't need to include the ensure source generator run just to get the analyser to work.
				.WithAdditionalSources(AggregateAttributeEmitter.Emit().Select(m => m.Source))
				// This just makes the test code cleaner, otherwise we have to include the namespaces in the source code.
				.WithAdditionalNamespaces(TypeLibrary.AggregateNamespace, "Purview.ValueObjects.Serialization"),
			cancellationToken
		);
	}
}
