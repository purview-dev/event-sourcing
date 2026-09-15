using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Purview.EventSourcing.SourceGenerator.Generators;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
	"Maintainability",
	"CA1506:Avoid excessive class coupling",
	Justification = "Aggregate source-generator suite covers the full generated surface."
)]
public sealed class AggregateSourceGeneratorTests : AggregateSourceGeneratorTestBase
{
	[Test]
	public async Task Generate_GivenEmptySource_GeneratesAttributesOnly(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	public class Empty { }
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert
		await Assert
			.That(result.AllSyntaxTrees.Length)
			.IsEqualTo(EventSourcingGeneratorTestOptions.AggregateExpectedFileCount);
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_GeneratesExpectedCode(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }
		public decimal Total { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId, decimal total);

		[Event]
		public partial void UpdateTotal(decimal total);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that 4 attribute files + 1 generated aggregate file
		await Assert
			.That(result.AllSyntaxTrees.Length)
			.IsEqualTo(EventSourcingGeneratorTestOptions.AggregateExpectedFileCountPlusGen);
	}

	[Test]
	public async Task Generate_GivenAggregateWithNoEvents_GeneratesEmptyRegisterEvents(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class EmptyAggregate : AggregateBase
	{
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert
		await Assert
			.That(result.AllSyntaxTrees.Length)
			.IsEqualTo(EventSourcingGeneratorTestOptions.AggregateExpectedFileCountPlusGen);
	}

	[Test]
	public async Task Generate_GivenAggregateWithParameterlessEvent_GeneratesCorrectly(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class CounterAggregate : AggregateBase
	{
		public int Count { get; private set; }

		[Event]
		public partial void Increment();
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert
		await Assert
			.That(result.AllSyntaxTrees.Length)
			.IsEqualTo(EventSourcingGeneratorTestOptions.AggregateExpectedFileCountPlusGen);
	}

	[Test]
	public async Task Generate_GivenNonPartialClass_DoesNotGenerate(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public class NonPartialAggregate : AggregateBase
	{
		protected override void RegisterEvents() { }
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that only attribute files, no generated aggregate
		await Assert
			.That(result.AllSyntaxTrees.Length)
			.IsEqualTo(EventSourcingGeneratorTestOptions.AggregateExpectedFileCount);
		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.AggregateMustBePartial);
	}

	[Test]
	public async Task Generate_GivenInvalidAggregate_WithGeneratorOnly_ProducesNoOutputAndNoExceptions(
		CancellationToken cancellationToken
	)
	{
		// The generator consumes the same shared validation as the analyzer: when validation fails it
		// must skip generation entirely rather than emit an invalid partial. Diagnostics are owned by
		// the analyzer, so a generator-only run reports nothing but must not throw or generate.
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public class NonPartialAggregate : AggregateBase
	{
		protected override void RegisterEvents() { }
	}
}
";

		var generatorOnlyOptions = EventSourcingGeneratorTestOptions.Default with { AnalyzerTypes = [] };
		var result = await GenerateAsync(source, generatorOnlyOptions, cancellationToken);

		await Assert
			.That(result.AllSyntaxTrees.Length)
			.IsEqualTo(EventSourcingGeneratorTestOptions.AggregateExpectedFileCount);
		foreach (var genResult in result.DriverResult.Results)
			await Assert.That(genResult.Exception).IsNull();
		await Assert.That(result.Generated().HasClass("NonPartialAggregate", "Testing")).IsFalse();
	}

	[Test]
	public async Task Generate_GivenInvalidAggregate_AnalyzerReportsAndGeneratorSkips(
		CancellationToken cancellationToken
	)
	{
		// The consistency contract: the analyzer surfaces the shared-validation diagnostic while the
		// generator (driven by the same rules) emits no aggregate output.
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public class NonPartialAggregate : AggregateBase
	{
		protected override void RegisterEvents() { }
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.AggregateMustBePartial);
		await Assert
			.That(result.AllSyntaxTrees.Length)
			.IsEqualTo(EventSourcingGeneratorTestOptions.AggregateExpectedFileCount);
		await Assert.That(result.Generated().HasClass("NonPartialAggregate", "Testing")).IsFalse();
	}

	[Test]
	public async Task Generate_GivenMultipleParameters_GeneratesAllProperties(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ProductAggregate : AggregateBase
	{
		public string Name { get; private set; }
		public decimal Price { get; private set; }
		public int Quantity { get; private set; }

		[Event]
		public partial void SetProduct(string name, decimal price, int quantity);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert
		await Assert
			.That(result.AllSyntaxTrees.Length)
			.IsEqualTo(EventSourcingGeneratorTestOptions.AggregateExpectedFileCountPlusGen);
	}

	[Test]
	public async Task Generate_GivenComputedParameterWithNoComputeHook_ReportsDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public enum ReportProcessingStatus
	{
		Uploaded,
		Complete,
		Failed
	}

	[Aggregate]
	public partial class ReportUploadAggregate : AggregateBase
	{
		public string Blob { get; private set; }
		public object Summary { get; private set; }
		public ReportProcessingStatus Status { get; private set; }

		[Event(EventName = ""CompletedEvent"")]
		public partial ReportUploadAggregate MarkAsCompleted(
			string blob,
			object summary,
			[Computed] ReportProcessingStatus status = default
		);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert
			.That(result)
			.DoesNotHaveDiagnostic(DiagnosticLibrary.AggregatePropertyCollectionTypeMustUseEventStoreCollections);
		await Assert
			.That(result)
			.DoesNotHaveDiagnostic(DiagnosticLibrary.NullableScalarEqualityNullComparisonShouldUsePatternMatching);
	}

	[Test]
	public async Task Generate_GivenNullableScalarComparedToNullWithPatternMatching_DoesNotReportWarning(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Scalar]
	public readonly partial record struct ProjectId
	{
		public string Value { get; }
		private ProjectId(string value) => Value = value;
	}

	[Aggregate]
	public partial class ReportAggregate : AggregateBase
	{
		public string Name { get; private set; } = string.Empty;

		[Event]
		public partial void SetName(string name);

		public bool ShouldClear(ProjectId? projectId) => projectId is null;
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert
			.That(result)
			.DoesNotHaveDiagnostic(DiagnosticLibrary.NullableScalarEqualityNullComparisonShouldUsePatternMatching);
	}

	[Test]
	public async Task Generate_GivenScalarWrappingComplexValue_ReportsQueryTranslationWarning(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public sealed class ReportSummary
	{
		public int FailedLines { get; set; }
	}

	[Scalar]
	public readonly partial record struct ReportSummaryScalar
	{
		public ReportSummary Value { get; }
		private ReportSummaryScalar(ReportSummary value) => Value = value;
	}

	[Aggregate]
	public partial class ReportAggregate : AggregateBase
	{
		public ReportSummaryScalar Summary { get; private set; }

		[Event]
		public partial void SetSummary(ReportSummary value);
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);
		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.ScalarComplexValueMayNotTranslateInSQLSnapshots);
	}

	[Test]
	public async Task Generate_GivenScalarWrappingPrimitiveValue_DoesNotReportQueryTranslationWarning(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Scalar]
	public readonly partial record struct ProjectId
	{
		public string Value { get; }
		private ProjectId(string value) => Value = value;
		public static ProjectId Create(string value) => new(value);
	}

	[Aggregate]
	public partial class ReportAggregate : AggregateBase
	{
		public ProjectId ProjectId { get; private set; }

		[Event]
		public partial void SetProjectId(string projectId);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert
			.That(result)
			.DoesNotHaveDiagnostic(DiagnosticLibrary.ScalarComplexValueMayNotTranslateInSQLSnapshots);
	}

	[Test]
	public async Task Generate_GivenComputedParameterWithOnlyOnComputingHook_DoesNotReportHookDiagnostics(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public enum ReportProcessingStatus
	{
		Uploaded,
		Complete,
		Failed
	}

	[Aggregate]
	public partial class ReportUploadAggregate : AggregateBase
	{
		public string Blob { get; private set; }
		public object Summary { get; private set; }
		public ReportProcessingStatus Status { get; private set; }

		[Event(EventName = ""CompletedEvent"")]
		public partial ReportUploadAggregate MarkAsCompleted(
			string blob,
			object summary,
			[Computed] ReportProcessingStatus status = default
		);

		partial void OnComputingCompletedEvent(ref ReportProcessingStatus status) => status = ReportProcessingStatus.Complete;
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE018");
		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE019");

		await Assert.That(result.EnsureValid).ThrowsNothing();
	}

	[Test]
	public async Task Generate_GivenComputedParameterWithValidOnComputingSignature_DoesNotReportUnrelatedDiagnostics(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public enum ReportProcessingStatus
	{
		Uploaded,
		Complete,
		Failed
	}

	[Aggregate]
	public partial class ReportUploadAggregate : AggregateBase
	{
		public string Blob { get; private set; }
		public object Summary { get; private set; }
		public ReportProcessingStatus Status { get; private set; }

		[Event(EventName = ""CompletedEvent"")]
		public partial ReportUploadAggregate MarkAsCompleted(
			string blob,
			object summary,
			[Computed] ReportProcessingStatus status = default
		);

		partial void OnComputingCompletedEvent(ref ReportProcessingStatus status) { }
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert
			.That(result)
			.DoesNotHaveDiagnostic(DiagnosticLibrary.AggregatePropertyCollectionTypeMustUseEventStoreCollections);
		await Assert
			.That(result)
			.DoesNotHaveDiagnostic(DiagnosticLibrary.NullableScalarEqualityNullComparisonShouldUsePatternMatching);
	}

	[Test]
	public async Task Generate_GivenComputedParameter_GeneratedSourceContainsComputingAndRaisingHookOverloads(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public enum ReportProcessingStatus
	{
		Uploaded,
		Complete,
		Failed
	}

	[Aggregate]
	public partial class ReportUploadAggregate : AggregateBase
	{
		public string Blob { get; private set; }
		public object Summary { get; private set; }
		public ReportProcessingStatus Status { get; private set; }

		[Event(EventName = ""CompletedEvent"")]
		public partial ReportUploadAggregate MarkAsCompleted(
			string blob,
			object summary,
			[Computed] ReportProcessingStatus status = default
		);

		partial void OnComputingCompletedEvent(ref ReportProcessingStatus status) => status = ReportProcessingStatus.Complete;
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("ReportUploadAggregate", "Testing");
		var statusType = TypeRefs.Named("ReportProcessingStatus", "Testing");

		var computingSingle = aggregate.GetMethod("OnComputingCompletedEvent", statusType);
		await Assert.That(computingSingle.Node.Modifiers.ToString()).Contains("partial");

		var computingAll = aggregate.GetMethod(
			"OnComputingCompletedEvent",
			TypeRefs.String,
			TypeRefs.Object,
			statusType
		);
		await Assert.That(computingAll.Node.Modifiers.ToString()).Contains("partial");

		var raisingNonComputed = aggregate.GetMethod("OnRaisingCompletedEvent", TypeRefs.String, TypeRefs.Object);
		await Assert.That(raisingNonComputed.Node.Modifiers.ToString()).Contains("partial");

		var raisingAll = aggregate.GetMethod("OnRaisingCompletedEvent", TypeRefs.String, TypeRefs.Object, statusType);
		await Assert.That(raisingAll.Node.Modifiers.ToString()).Contains("partial");
	}

	[Test]
	public async Task Generate_GivenComputedParameterWithOnlyOnComputingWithoutComputedValuesHook_DoesNotReportHookDiagnostics(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public enum ReportProcessingStatus
	{
		Uploaded,
		Complete,
		Failed
	}

	[Aggregate]
	public partial class ReportUploadAggregate : AggregateBase
	{
		public string Blob { get; private set; }
		public object Summary { get; private set; }
		public ReportProcessingStatus Status { get; private set; }

		[Event(EventName = ""CompletedEvent"")]
		public partial ReportUploadAggregate MarkAsCompleted(
			string blob,
			object summary,
			[Computed] ReportProcessingStatus status = default
		);

		partial void OnComputingCompletedEvent(ref string blob, ref object summary)
		{
		}
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE018");
		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE019");
	}

	[Test]
	public async Task Generate_GivenEventNameOverride_HookNamesUseOverriddenEventName(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public enum ReportProcessingStatus
	{
		None,
		Complete
	}

	[Aggregate]
	public partial class ReportUploadAggregate : AggregateBase
	{
		public string Blob { get; private set; }
		public object Summary { get; private set; }
		public ReportProcessingStatus Status { get; private set; }

		[Event(EventName = ""MarkAsCompleted"")]
		public partial ReportUploadAggregate MarkAsComplete(
			string blob,
			object summary,
			[Computed] ReportProcessingStatus status = default
		);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("ReportUploadAggregate", "Testing");
		var statusType = TypeRefs.Named("ReportProcessingStatus", "Testing");
		await Assert.That(aggregate.HasMethod("OnComputingMarkAsCompletedEvent", statusType)).IsTrue();
		await Assert
			.That(aggregate.HasMethod("OnRaisingMarkAsCompletedEvent", TypeRefs.String, TypeRefs.Object, statusType))
			.IsTrue();
		await Assert
			.That(aggregate.HasMethod("OnRaisingMarkAsCompletedEvent", TypeRefs.String, TypeRefs.Object))
			.IsTrue();
		await Assert
			.That(aggregate.HasMethod("OnComputingMarkAsCompletedEvent", TypeRefs.String, TypeRefs.Object, statusType))
			.IsTrue();
	}

	[Test]
	public async Task Generate_ProducesNoDiagnosticErrors(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class SimpleAggregate : AggregateBase
	{
		public string Value { get; private set; }

		[Event]
		public partial void SetValue(string value);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that no generator exceptions
		foreach (var genResult in result.DriverResult.Results)
		{
			await Assert.That(genResult.Exception).IsNull();
		}

		// Assert that no errors in the output compilation (excluding pre-existing diagnostic warnings)
		var errors = result
			.CompilationResult.Compilation.GetDiagnostics(cancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();

		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_GeneratedSourceContainsEventClass(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that event class uses the default namespace pattern and is a [EventContract] record
		await Assert.That(result.Generated().HasNamespace("Testing.OrderEvents")).IsTrue();
		var orderCreated = result.Generated().GetRecord("OrderCreatedEvent", "Testing.OrderEvents");
		await Assert.That(orderCreated.Node.AttributeLists.ToString()).Contains("EventContract");
		await Assert.That(orderCreated.Node.BaseList).IsNull();
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_GeneratedSourceContainsJsonConverterSupport(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result.Generated().HasClass("OrderAggregateJsonConverter", "Testing")).IsTrue();
		await Assert.That(result.Generated().HasClass("OrderAggregateJsonModel", "Testing")).IsTrue();

		var query = result.Generated();
		var aggregate = query.GetClass("OrderAggregate", "Testing");
		var createFromJsonModel = aggregate.GetMethod("CreateFromJsonModel");
		await Assert.That(createFromJsonModel.Node.Modifiers.ToString()).Contains("static");
		await Assert.That(createFromJsonModel.Node.ParameterList.Parameters.Count).IsEqualTo(1);
		await Assert
			.That(createFromJsonModel.Node.ParameterList.Parameters[0].Type?.ToString())
			.Contains("OrderAggregateJsonModel");
		await Assert.That(aggregate.Node.AttributeLists.ToString()).Contains("JsonConverter");
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_GeneratedSourceContainsEventProperties(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }
		public decimal Total { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId, decimal total);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var orderCreated = query.GetRecord("OrderCreatedEvent", "Testing.OrderEvents");
		await Assert.That(orderCreated.HasProperty("CustomerId", TypeRefs.String)).IsTrue();
		await Assert.That(orderCreated.HasProperty("Total", TypeRefs.Decimal)).IsTrue();
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_GeneratedSourceContainsRecordEvent(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string Name { get; private set; }
		public int Count { get; private set; }

		[Event]
		public partial void SetOrder(string name, int count);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		// Events are [EventContract] records: equality/hash is compiler-generated, so there is no
		// BuildEventHash override.
		var orderSet = query.GetRecord("OrderSetEvent", "Testing.OrderEvents");
		await Assert.That(orderSet.Node.BaseList).IsNull();
		await Assert.That(orderSet.HasProperty("Name", TypeRefs.String)).IsTrue();
		await Assert.That(orderSet.HasProperty("Count", TypeRefs.Int)).IsTrue();
		await Assert.That(orderSet.HasMethod("BuildEventHash", TypeRefs.HashCode)).IsFalse();
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_GeneratedSourceContainsRegisterEvents(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId);

		[Event]
		public partial void UpdateOrder(string customerId);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("OrderAggregate", "Testing");
		var registerEvents = aggregate.GetMethod("RegisterEvents");
		var modifiers = registerEvents.Node.Modifiers.ToString();
		await Assert.That(modifiers).Contains("override");
		await Assert.That(modifiers).Contains("protected");

		var registerBody = registerEvents.Node.Body?.ToString() ?? string.Empty;
		await Assert.That(registerBody).Contains("RegisterGenerated<global::Testing.OrderEvents.OrderCreatedEvent>();");
		await Assert.That(registerBody).Contains("RegisterGenerated<global::Testing.OrderEvents.OrderUpdatedEvent>();");
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_GeneratedSourceContainsApplyMethods(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("OrderAggregate", "Testing");
		await Assert
			.That(aggregate.HasMethod("Apply", TypeRefs.Event("OrderCreatedEvent", "Testing.OrderEvents")))
			.IsTrue();

		var apply = aggregate.GetMethod("Apply", TypeRefs.Event("OrderCreatedEvent", "Testing.OrderEvents"));
		await Assert.That(apply.Node.Body?.ToString()).Contains("CustomerId = @event.CustomerId;");
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_GeneratedSourceContainsCommandMethod(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }
		public decimal Total { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId, decimal total);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("OrderAggregate", "Testing");
		var createOrder = aggregate.GetMethod("CreateOrder", TypeRefs.String, TypeRefs.Decimal);
		var modifiers = createOrder.Node.Modifiers.ToString();
		await Assert.That(modifiers).Contains("partial");
		await Assert.That(modifiers).Contains("public");

		var body = createOrder.Node.Body?.ToString() ?? string.Empty;
		await Assert.That(body).Contains("var @event = new global::Testing.OrderEvents.OrderCreatedEvent");
		await Assert.That(body).Contains("RecordAndApply(@event);");
		await Assert.That(body).Contains("CustomerId = customerId,");
		await Assert.That(body).Contains("Total = total,");
	}

	[Test]
	public async Task Generate_GivenStringMappedEvent_GeneratedSourceContainsOrdinalGuard(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ProfileAggregate : AggregateBase
	{
		public string Name { get; private set; } = default!;

		[Event]
		public partial void Rename(string name);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("ProfileAggregate", "Testing");
		var rename = aggregate.GetMethod("Rename", TypeRefs.String);
		await Assert.That(rename.Node.Modifiers.ToString()).Contains("partial");
		await Assert
			.That(rename.Node.Body?.ToString())
			.Contains("if (global::System.String.Equals(Name, name, global::System.StringComparison.Ordinal))");
	}

	[Test]
	public async Task Generate_GivenMultiPropertyEvent_GeneratedSourceContainsCompoundEqualityGuard(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ProductAggregate : AggregateBase
	{
		public string Name { get; private set; } = default!;
		public int Quantity { get; private set; }

		[Event]
		public partial void Update(string name, int quantity);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("ProductAggregate", "Testing");
		var update = aggregate.GetMethod("Update", TypeRefs.String, TypeRefs.Int);
		var body = update.Node.Body?.ToString() ?? string.Empty;
		await Assert
			.That(body)
			.Contains(
				"global::System.String.Equals(Name, name, global::System.StringComparison.Ordinal) && global::System.Collections.Generic.EqualityComparer<int>.Default.Equals(Quantity, quantity)"
			);
		await Assert.That(body).Contains("return;");
	}

	[Test]
	public async Task Generate_GivenAggregateReturningEventMethod_GeneratedSourceSupportsFluentChaining(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ProfileAggregate : AggregateBase
	{
		public string Name { get; private set; } = default!;

		[Event]
		public partial ProfileAggregate Rename(string name);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("ProfileAggregate", "Testing");
		var rename = aggregate.GetMethod("Rename", TypeRefs.String);
		var modifiers = rename.Node.Modifiers.ToString();
		await Assert.That(modifiers).Contains("partial");
		await Assert.That(modifiers).Contains("public");
		var returnType = rename.Node.ReturnType.ToString();
		await Assert.That(returnType).Contains("ProfileAggregate");
		await Assert
			.That(rename.Node.Body?.ToString())
			.Contains("if (global::System.String.Equals(Name, name, global::System.StringComparison.Ordinal))");
		await Assert.That(rename.Node.Body?.ToString()).Contains("return this;");
	}

	[Test]
	public async Task Generate_GivenBoolReturningEventMethod_GeneratedSourceReturnsFalseWhenUnchanged(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ProfileAggregate : AggregateBase
	{
		public string Name { get; private set; } = default!;

		[Event]
		public partial bool Rename(string name);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("ProfileAggregate", "Testing");
		var rename = aggregate.GetMethod("Rename", TypeRefs.String);
		var modifiers = rename.Node.Modifiers.ToString();
		await Assert.That(modifiers).Contains("partial");
		await Assert.That(modifiers).Contains("public");
		await Assert.That(rename.Node.ReturnType.ToString()).Contains("bool");
		await Assert
			.That(rename.Node.Body?.ToString())
			.Contains("if (global::System.String.Equals(Name, name, global::System.StringComparison.Ordinal))");
		await Assert.That(rename.Node.Body?.ToString()).Contains("return false;");
		await Assert.That(rename.Node.Body?.ToString()).Contains("return true;");
	}

	[Test]
	public async Task Generate_GivenParameterlessEvent_GeneratedSourceContainsEmptyEventAndRecordAndApplyWithNew(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class CounterAggregate : AggregateBase
	{
		public int Count { get; private set; }

		[Event]
		public partial void Increment();
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);
		var query = result.Generated();
		var errors = result
			.CompilationResult.Compilation.GetDiagnostics(cancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();

		var aggregate = query.GetClass("CounterAggregate", "Testing");
		var increment = aggregate.GetMethod("Increment");
		await Assert.That(increment.Node.Modifiers.ToString()).Contains("partial");
		await Assert
			.That(aggregate.HasMethod("Apply", TypeRefs.Event("IncrementedEvent", "Testing.CounterEvents")))
			.IsTrue();

		var incrementedEvent = query.GetRecord("IncrementedEvent", "Testing.CounterEvents");
		await Assert.That(incrementedEvent.Node.BaseList).IsNull();
		await Assert.That(incrementedEvent.HasMethod("BuildEventHash", TypeRefs.HashCode)).IsFalse();

		var body = increment.Node.Body?.ToString() ?? string.Empty;
		await Assert.That(body).Contains("var @event = new global::Testing.CounterEvents.IncrementedEvent");
		await Assert.That(body).Contains("RecordAndApply(@event);");
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task Generate_GivenImplicitlyConvertibleParameterType_GeneratedSourceUsesPropertyTypeForEqualityGuard(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public readonly record struct Name
	{
		public string Value { get; }

		private Name(string value) => Value = value;

		public static implicit operator Name(string value) => new(value);
	}

	[Aggregate]
	public partial class CustomerAggregate : AggregateBase
	{
		public Name Name { get; private set; }

		[Event]
		public partial void ChangeName(string name);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("CustomerAggregate", "Testing");
		var changeName = aggregate.GetMethod("ChangeName", TypeRefs.String);
		await Assert
			.That(changeName.Node.Body?.ToString())
			.Contains(
				"global::System.Collections.Generic.EqualityComparer<global::Testing.Name>.Default.Equals(Name, __nameValue)"
			);
	}

	[Test]
	public async Task Generate_GivenPrivatePartialEventMethod_GeneratesPrivateMethodAndPublicEvent(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ToggleAggregate : AggregateBase
	{
		public bool IsActive { get; private set; }

		[Event]
		private partial ToggleAggregate ChangeIsActive(bool isActive);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("ToggleAggregate", "Testing");
		var changeIsActive = aggregate.GetMethod("ChangeIsActive", TypeRefs.Bool);
		var modifiers = changeIsActive.Node.Modifiers.ToString();
		await Assert.That(modifiers).Contains("partial");
		await Assert.That(modifiers).Contains("private");
		await Assert.That(result.Generated().HasRecord("IsActiveChangedEvent", "Testing.ToggleEvents")).IsTrue();
	}

	[Test]
	public async Task Generate_GivenAggregateWithNoEvents_GeneratedSourceContainsEmptyRegisterEvents(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class EmptyAggregate : AggregateBase
	{
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("EmptyAggregate", "Testing");
		var registerEvents = aggregate.GetMethod("RegisterEvents");
		var modifiers = registerEvents.Node.Modifiers.ToString();
		await Assert.That(modifiers).Contains("override");
		await Assert.That(modifiers).Contains("protected");
		await Assert.That(query.HasNamespace("Testing.EmptyEvents")).IsFalse();
	}

	[Test]
	public async Task Generate_GivenMultipleEvents_GeneratedSourceContainsAllEventClasses(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }
		public decimal Total { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId, decimal total);

		[Event]
		public partial void UpdateTotal(decimal total);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that both event records exist in the default events namespace
		await Assert.That(result.Generated().HasRecord("OrderCreatedEvent", "Testing.OrderEvents")).IsTrue();
		await Assert.That(result.Generated().HasRecord("TotalUpdatedEvent", "Testing.OrderEvents")).IsTrue();
	}

	[Test]
	public async Task Generate_GivenClassWithNoBaseClass_GeneratesAndAddsAggregateBaseToGeneratedPart(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing;

[Aggregate]
public partial class NotAnAggregate
{
	public string Value { get; private set; }

	[Event]
	public partial void DoSomething(string value);
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result.DriverResult.GeneratedTrees).Count().IsEqualTo(ExpectedFileCountPlusGen);

		var notAnAggregate = result.Generated().GetClass("NotAnAggregate", "Testing");
		await Assert.That(notAnAggregate.Node.Modifiers.ToString()).Contains("partial");
		await Assert.That(notAnAggregate.Node.BaseList?.ToString()).Contains("AggregateBase");
		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.AggregateMustInheritAggregateBase);
	}

	[Test]
	public async Task Generate_GivenNonPartialMethod_MethodIsSkipped(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class MixedAggregate : AggregateBase
	{
		public string Name { get; private set; }

		[Event]
		public partial void SetName(string name);

		// This method is NOT partial, so it should be ignored even though it has the attribute
		[Event]
		public void NonPartialMethod(string value)
		{
			// Ignore me!
		}
	}
}
";

		// Act
		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result.Generated().HasRecord("NonPartialMethodEvent", "Testing.MixedEvents")).IsFalse();
		await Assert.That(result).HasDiagnostic("EVENTSTORE007");
	}

	[Test]
	public async Task Generate_GivenClassWithOnlyInterfaces_GeneratesAndAddsAggregateBaseToGeneratedPart(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public interface ITaggable
	{
	}

	[Aggregate]
	public partial class InterfaceOnlyAggregate : ITaggable
	{
		public string Value { get; private set; } = string.Empty;

		[Event]
		public partial void SetValue(string value);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var interfaceOnlyAggregate = result.Generated().GetClass("InterfaceOnlyAggregate", "Testing");
		await Assert.That(interfaceOnlyAggregate.Node.BaseList?.ToString()).Contains("AggregateBase");
		await Assert.That(result).DoesNotHaveDiagnostic("EVENTSTORE002");

		var errors = result
			.CompilationResult.Compilation.GetDiagnostics(cancellationToken)
			.Where(static d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();
		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task Generate_GivenInternalAggregate_GeneratesInternalAccessModifier(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	internal partial class InternalAggregate : AggregateBase
	{
		public string Value { get; private set; }

		[Event]
		public partial void SetValue(string value);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that the generated partial class uses 'internal' access modifier
		var internalAggregate = result.Generated().GetClass("InternalAggregate", "Testing");
		await Assert.That(internalAggregate.Node.Modifiers.ToString()).Contains("internal");
		await Assert.That(internalAggregate.Node.Modifiers.ToString()).Contains("partial");
	}

	[Test]
	public async Task Generate_GivenAttributeFiles_ContainsAggregateAttribute(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	public class Empty { }
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that attribute files are generated
		await Assert.That(result.DriverResult.GeneratedTrees).Count().IsEqualTo(ExpectedFileCount);

		const string aggregateNamespace = "Purview.EventSourcing.Aggregates";
		var query = result.Generated();
		await Assert.That(query.HasClass("EmbeddedAttribute", "Microsoft.CodeAnalysis")).IsTrue();
		await Assert.That(query.HasClass("PropertyAttribute", aggregateNamespace)).IsTrue();
		await Assert.That(query.HasClass("AggregateAttribute", aggregateNamespace)).IsTrue();
		await Assert.That(query.HasClass("AggregateDefaultsAttribute", aggregateNamespace)).IsTrue();
		await Assert.That(query.HasClass("EventAttribute", aggregateNamespace)).IsTrue();
		await Assert.That(query.HasClass("MetadataAttribute", aggregateNamespace)).IsTrue();
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_OutputCompilationHasNoErrors(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }
		public decimal Total { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId, decimal total);

		[Event]
		public partial void UpdateTotal(decimal total);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that no generator exceptions
		foreach (var genResult in result.DriverResult.Results)
		{
			await Assert.That(genResult.Exception).IsNull();
		}

		// Assert that no compilation errors
		var errors = result
			.CompilationResult.Compilation.GetDiagnostics(cancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();

		await Assert.That(errors).IsEmpty();
	}

	[Test]
	public async Task Generate_GivenGeneratedFile_HasAutoGeneratedHeader(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class SimpleAggregate : AggregateBase
	{
		[Event]
		public partial void DoWork();
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);
		var generatedSource = result.GetSource();

		// Assert that generated file starts with auto-generated header
		await Assert.That(generatedSource).ContainsGeneratedCode("// <auto-generated />");
		await Assert.That(generatedSource).ContainsGeneratedCode("#nullable enable");
	}

	[Test]
	public async Task Generate_GivenEventWithDefaultVersion_GeneratesStaticSchemaVersionOfOne(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that default version is 1
		var query = result.Generated();
		var schemaVersion = query
			.GetRecord("OrderCreatedEvent", "Testing.OrderEvents")
			.GetProperty("SchemaVersion", TypeRefs.Int);
		await Assert.That(schemaVersion.Node.Modifiers.ToString()).Contains("static");
		await Assert.That(schemaVersion.Node.ExpressionBody?.ToString()).Contains("1");
	}

	[Test]
	public async Task Generate_GivenEventWithExplicitVersion_GeneratesCorrectStaticSchemaVersion(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }

		[Event(Version = 3)]
		public partial void CreateOrder(string customerId);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that explicit version 3
		var query = result.Generated();
		var schemaVersion = query
			.GetRecord("OrderCreatedEvent", "Testing.OrderEvents")
			.GetProperty("SchemaVersion", TypeRefs.Int);
		await Assert.That(schemaVersion.Node.ExpressionBody?.ToString()).Contains("3");
	}

	[Test]
	public async Task Generate_GivenMultipleEventsWithDifferentVersions_GeneratesCorrectSchemaVersionForEach(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }
		public decimal Total { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId);

		[Event(Version = 2)]
		public partial void UpdateTotal(decimal total);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var orderCreatedSchemaVersion = query
			.GetRecord("OrderCreatedEvent", "Testing.OrderEvents")
			.GetProperty("SchemaVersion", TypeRefs.Int);
		var totalUpdatedSchemaVersion = query
			.GetRecord("TotalUpdatedEvent", "Testing.OrderEvents")
			.GetProperty("SchemaVersion", TypeRefs.Int);

		await Assert.That(orderCreatedSchemaVersion.Node.ExpressionBody?.ToString()).Contains("1");
		await Assert.That(totalUpdatedSchemaVersion.Node.ExpressionBody?.ToString()).Contains("2");
		// They should appear in event-declaration order (OrderCreated before TotalUpdated)
		await Assert
			.That(orderCreatedSchemaVersion.Node.SpanStart)
			.IsLessThan(totalUpdatedSchemaVersion.Node.SpanStart);
	}

	[Test]
	public async Task Generate_GivenEventWithNonPositiveVersion_ReportsVersionDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing;

[Aggregate]
public partial class OrderAggregate : AggregateBase
{
	public string CustomerId { get; private set; }

	[Event(Version = 0)]
	public partial void CreateOrder(string customerId);
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.EventSchemaVersionMustBePositive);
	}

	[Test]
	public async Task Generate_GivenMultipleEventsWithDuplicateVersions_ReportsDuplicateVersionDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }
		public decimal Total { get; private set; }

		[Event(Version = 2)]
		public partial void CreateOrder(string customerId);

		[Event(Version = 2)]
		public partial void UpdateTotal(decimal total);
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.DuplicateEventSchemaVersionOnAggregate);
	}

	[Test]
	public async Task Generate_GivenVersionedEvent_GeneratedAttributeTemplateContainsVersionProperty(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	public class Empty { }
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		const string aggregateNamespace = "Purview.EventSourcing.Aggregates";
		var query = result.Generated();
		var eventAttribute = query.GetClass("EventAttribute", aggregateNamespace);
		var aggregateAttribute = query.GetClass("AggregateAttribute", aggregateNamespace);
		var aggregateDefaultsAttribute = query.GetClass("AggregateDefaultsAttribute", aggregateNamespace);

		await Assert.That(eventAttribute.HasProperty("Version", TypeRefs.Int)).IsTrue();
		await Assert.That(eventAttribute.HasProperty("EventName", TypeRefs.String)).IsTrue();
		await Assert.That(eventAttribute.HasProperty("EventNamespace", TypeRefs.String)).IsTrue();
		await Assert.That(aggregateAttribute.HasProperty("EventNamespace", TypeRefs.String)).IsTrue();
		await Assert.That(aggregateAttribute.HasProperty("EventSuffix", TypeRefs.String)).IsTrue();
		await Assert.That(aggregateDefaultsAttribute.HasProperty("EventSuffix", TypeRefs.String)).IsTrue();
	}

	[Test]
	public async Task Generate_GivenInferredEventName_AppliesSuffixByDefault(CancellationToken cancellationToken)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = string.Empty;

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		await Assert.That(query.HasRecord("OrderCreatedEvent", "Testing.OrderEvents")).IsTrue();

		var aggregate = query.GetClass("OrderAggregate", "Testing");
		var registerBody = aggregate.GetMethod("RegisterEvents").Node.Body?.ToString() ?? string.Empty;
		await Assert.That(registerBody).Contains("RegisterGenerated<global::Testing.OrderEvents.OrderCreatedEvent>();");
		await Assert
			.That(aggregate.HasMethod("Apply", TypeRefs.Event("OrderCreatedEvent", "Testing.OrderEvents")))
			.IsTrue();
	}

	[Test]
	public async Task Generate_GivenAssemblyEventSuffixOverride_UsesAssemblyConfiguredSuffix(
		CancellationToken cancellationToken
	)
	{
		var source =
			@"[assembly: AggregateDefaults(EventSuffix = ""DomainEvent"")]

namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = string.Empty;

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		await Assert.That(query.HasRecord("OrderCreatedDomainEvent", "Testing.OrderEvents")).IsTrue();

		var aggregate = query.GetClass("OrderAggregate", "Testing");
		var registerBody = aggregate.GetMethod("RegisterEvents").Node.Body?.ToString() ?? string.Empty;
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.OrderEvents.OrderCreatedDomainEvent>();");
		await Assert
			.That(aggregate.HasMethod("Apply", TypeRefs.Event("OrderCreatedDomainEvent", "Testing.OrderEvents")))
			.IsTrue();
	}

	[Test]
	public async Task Generate_GivenAggregateEventSuffixOverride_PrefersAggregateSuffixOverAssembly(
		CancellationToken cancellationToken
	)
	{
		var source =
			@"[assembly: AggregateDefaults(EventSuffix = ""DomainEvent"")]

namespace Testing
{
	[Aggregate(EventSuffix = ""CustomEvent"")]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = string.Empty;

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		await Assert.That(query.HasRecord("OrderCreatedCustomEvent", "Testing.OrderEvents")).IsTrue();

		var aggregate = query.GetClass("OrderAggregate", "Testing");
		var registerBody = aggregate.GetMethod("RegisterEvents").Node.Body?.ToString() ?? string.Empty;
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.OrderEvents.OrderCreatedCustomEvent>();");
		await Assert
			.That(aggregate.HasMethod("Apply", TypeRefs.Event("OrderCreatedCustomEvent", "Testing.OrderEvents")))
			.IsTrue();
	}

	[Test]
	public async Task Generate_GivenAggregateEventNamespaceOverride_UsesConfiguredNamespace(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate(EventNamespace = ""Testing.Custom.Events"")]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = string.Empty;

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		await Assert.That(query.HasNamespace("Testing.Custom.Events")).IsTrue();

		var aggregate = query.GetClass("OrderAggregate", "Testing");
		var registerBody = aggregate.GetMethod("RegisterEvents").Node.Body?.ToString() ?? string.Empty;
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.Custom.Events.OrderCreatedEvent>();");
		await Assert
			.That(aggregate.HasMethod("Apply", TypeRefs.Event("OrderCreatedEvent", "Testing.Custom.Events")))
			.IsTrue();
	}

	[Test]
	public async Task Generate_GivenEventNameAndNamespaceOverride_UsesMethodOverrides(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate(EventNamespace = ""Testing.Custom.Events"")]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = string.Empty;

		[Event(EventName = ""OrderCreated"", EventNamespace = ""Testing.Domain.Ordering"")]
		public partial void CreateOrder(string customerId);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		await Assert.That(query.HasNamespace("Testing.Domain.Ordering")).IsTrue();
		await Assert.That(query.HasRecord("OrderCreated", "Testing.Domain.Ordering")).IsTrue();

		var aggregate = query.GetClass("OrderAggregate", "Testing");
		var registerBody = aggregate.GetMethod("RegisterEvents").Node.Body?.ToString() ?? string.Empty;
		await Assert.That(registerBody).Contains("RegisterGenerated<global::Testing.Domain.Ordering.OrderCreated>();");
		await Assert
			.That(aggregate.HasMethod("Apply", TypeRefs.Event("OrderCreated", "Testing.Domain.Ordering")))
			.IsTrue();
	}

	[Test]
	public async Task Generate_GivenFalsePositiveAggregateBaseName_ReportsInheritanceDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing;

protected abstract class AggregateBase
{
}

[Aggregate]
public partial class NotARealAggregate : Testing.AggregateBase
{
	public string Name { get; private set; } = default!;

	[Event]
	public partial void Rename(string name);
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic("EVENTSTORE002");
	}

	[Test]
	public async Task Generate_GivenNestedAggregate_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source =
			@"
namespace Testing
{
	public static partial class AggregateContainer
	{
		[Aggregate]
		public partial class NestedAggregate : AggregateBase
		{
			public string Value { get; private set; } = default!;

			[Event]
			public partial void SetValue(string value);
		}
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic("EVENTSTORE003");
	}

	[Test]
	public async Task Generate_GivenGenericAggregate_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source =
			@"
namespace Testing;

[Aggregate]
public partial class GenericAggregate<TValue> : AggregateBase
{
	public TValue Value { get; private set; } = default!;

	[Event]
	public partial void SetValue(TValue value);
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic("EVENTSTORE004");
	}

	[Test]
	public async Task Generate_GivenManualRegisterEvents_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ManualRegistrationAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;

		protected override void RegisterEvents() { }

		[Event]
		public partial void SetValue(string value);
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic("EVENTSTORE005");
	}

	[Test]
	[Arguments("set")]
	[Arguments("protected set")]
	[Arguments("internal set")]
	[Arguments("protected internal set")]
	[Arguments("private protected set")]
	public async Task Generate_GivenPropertySetterIsNotPrivate_ReportsError(
		string setterAccess,
		CancellationToken cancellationToken
	)
	{
		var source =
			$@"
namespace Testing
{{
	[Aggregate]
	public partial class PublicSetterAggregate : AggregateBase
	{{
		public string Value {{ get; {setterAccess}; }} = default!;

		[Event]
		public partial void SetValue(string value);
	}}
}}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic("EVENTSTORE011");
	}

	[Test]
	public async Task Generate_GivenNonAggregateReturnTypeEventMethod_ReportsDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class InvalidSignatureAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;

		[Event]
		public partial string SetValue(string value);
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic("EVENTSTORE008");
	}

	[Test]
	public async Task Generate_GivenUnsupportedEventMethodSignature_ReportsDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class InvalidSignatureAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;

		[Event]
		public static partial string SetValue(string value);
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic("EVENTSTORE008");
	}

	[Test]
	public async Task Generate_GivenOverloadedEventMethods_ReportsDuplicateEventNameDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{ 
	[Aggregate]
	public partial class DuplicateEventAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;
		public int Count { get; private set; }

		[Event]
		public partial void Update(string value);

		[Event]
		public partial void Update(int count);
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.DuplicateGeneratedEventName);
	}

	[Test]
	public async Task Generate_GivenMissingPropertyMapping_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class MappingAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;

		[Event]
		public partial void Rename(string customerId);
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.EventParameterMustMapToWritableProperty);
	}

	[Test]
	public async Task Generate_GivenMetadataStoreTrue_AddsStoredEventPropertyWithoutAggregateMutation(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class MappingAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;

		[Event]
		public partial void Rename([Metadata] string initialPropertyToTest);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.EventParameterMustMapToWritableProperty);

		var query = result.Generated();
		var aggregate = query.GetClass("MappingAggregate", "Testing");
		var rename = aggregate.GetMethod("Rename", TypeRefs.String);
		await Assert.That(rename.Node.Modifiers.ToString()).Contains("partial");

		var renamedEvent = query.GetRecord("RenamedEvent", "Testing.MappingEvents");
		await Assert.That(renamedEvent.HasProperty("InitialPropertyToTest", TypeRefs.String)).IsTrue();

		var body = rename.Node.Body?.ToString() ?? string.Empty;
		await Assert.That(body).Contains("OnRaisingRenamedEvent(ref initialPropertyToTest);");
		await Assert.That(body).Contains("InitialPropertyToTest = initialPropertyToTest,");
		await Assert.That(body).Contains("OnRaisedRenamedEvent(@event);");
		await Assert.That(result).DoesNotHaveDiagnostic("CS8795");
	}

	[Test]
	public async Task Generate_GivenMetadataStoreFalse_PassesParameterToOnRaisingWithoutStoringAndStoring(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing;

[Aggregate]
public partial class MappingAggregate
{
	public string Value { get; private set; } = default!;

	[Event]
	public partial void Rename(
		[Metadata(false)] string correlationId,
		[Metadata] string correlationToStoreImplicitId,
		[Metadata(true)] string? correlationToStoreExplicitId
	);
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.EventParameterMustMapToWritableProperty);

		var query = result.Generated();
		var aggregate = query.GetClass("MappingAggregate", "Testing");
		var rename = aggregate.GetMethod("Rename", TypeRefs.String, TypeRefs.String, TypeRefs.String);
		await Assert.That(rename.Node.Modifiers.ToString()).Contains("partial");

		var onRaising = aggregate.GetMethod("OnRaisingRenamedEvent", TypeRefs.String, TypeRefs.String, TypeRefs.String);
		await Assert.That(onRaising.Node.Modifiers.ToString()).Contains("partial");

		var renamedEvent = query.GetRecord("RenamedEvent", "Testing.MappingEvents");
		await Assert.That(renamedEvent.HasProperty("CorrelationId")).IsFalse();
		await Assert.That(renamedEvent.HasProperty("CorrelationToStoreImplicitId", TypeRefs.String)).IsTrue();
		await Assert.That(renamedEvent.HasProperty("CorrelationToStoreExplicitId", TypeRefs.String)).IsTrue();

		await Assert
			.That(rename.Node.Body?.ToString())
			.Contains("var @event = new global::Testing.MappingEvents.RenamedEvent");
		await Assert.That(result).DoesNotHaveDiagnostic("CS8795");
	}

	[Test]
	public async Task Generate_GivenPropertyOverride_MapsParameterToSpecifiedProperty(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class MappingAggregate : AggregateBase
	{
		public int QuantityOnHand { get; private set; }

		[Event]
		public partial void ReceiveStock([Property(nameof(QuantityOnHand))] int initialQuantity);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var stockReceived = query.GetRecord("StockReceivedEvent", "Testing.MappingEvents");
		await Assert.That(stockReceived.HasProperty("InitialQuantity", TypeRefs.Int)).IsTrue();

		var aggregate = query.GetClass("MappingAggregate", "Testing");
		var apply = aggregate.GetMethod("Apply", TypeRefs.Event("StockReceivedEvent", "Testing.MappingEvents"));
		await Assert.That(apply.Node.Body?.ToString()).Contains("QuantityOnHand = @event.InitialQuantity;");

		var receiveStock = aggregate.GetMethod("ReceiveStock", TypeRefs.Int);
		await Assert
			.That(receiveStock.Node.Body?.ToString())
			.Contains("OnRaisingStockReceivedEvent(ref initialQuantity);");

		await Assert
			.That(
				result
					.CompilationResult.Compilation.GetDiagnostics(cancellationToken)
					.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
					.Select(static diagnostic => diagnostic.Id)
			)
			.DoesNotContain("CS8795");
	}

	[Test]
	public async Task Generate_GivenPropertyOverrideTargetMissing_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class MappingAggregate : AggregateBase
	{
		public int QuantityOnHand { get; private set; }

		[Event]
		public partial void ReceiveStock([Property(""MissingProperty"")] int initialQuantity);
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.EventParameterMustMapToWritableProperty);
	}

	[Test]
	public async Task Generate_GivenInitOnlyMappedProperty_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class InitOnlyAggregate : AggregateBase
	{
		public string Value { get; init; } = default!;

		[Event]
		public partial void SetValue(string value);
	}
}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).HasDiagnostic("EVENTSTORE010");
	}

	[Test]
	public async Task Generate_GivenSameAggregateNameInDifferentNamespaces_UsesUniqueHintNames(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace First
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;

		[Event]
		public partial void SetValue(string value);
	}
}

namespace Second
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;

		[Event]
		public partial void SetValue(string value);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);
		var aggregateTrees = result.PrimarySyntaxTrees;

		await Assert.That(aggregateTrees.Length).IsEqualTo(2);
	}

	[Test]
	public async Task Generate_GivenAggregateWithManyEvents_GeneratesAllEventsAndRegistrations(
		CancellationToken cancellationToken
	)
	{
		// Arrange ÔÇö aggregate with 5 events covering full lifecycle
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }
		public decimal Total { get; private set; }
		public string Status { get; private set; }
		public string ShippingAddress { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId);

		[Event]
		public partial void UpdateTotal(decimal total);

		[Event]
		public partial void SetShippingAddress(string shippingAddress);

		[Event]
		public partial void ConfirmOrder();

		[Event]
		public partial void CancelOrder();

		partial void Apply(global::Testing.OrderEvents.OrderConfirmedEvent @event)
		{
			Status = ""Confirmed"";
		}

		partial void Apply(global::Testing.OrderEvents.OrderCanceledEvent @event)
		{
			Status = ""Canceled"";
		}
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		const string eventsNamespace = "Testing.OrderEvents";
		await Assert.That(query.HasRecord("OrderCreatedEvent", eventsNamespace)).IsTrue();
		await Assert.That(query.HasRecord("TotalUpdatedEvent", eventsNamespace)).IsTrue();
		await Assert.That(query.HasRecord("ShippingAddressSetEvent", eventsNamespace)).IsTrue();
		await Assert.That(query.HasRecord("OrderConfirmedEvent", eventsNamespace)).IsTrue();
		await Assert.That(query.HasRecord("OrderCanceledEvent", eventsNamespace)).IsTrue();

		// Assert that all 5 RegisterGenerated calls
		var registerBody =
			query.GetClass("OrderAggregate", "Testing").GetMethod("RegisterEvents").Node.Body?.ToString()
			?? string.Empty;
		await Assert.That(registerBody).Contains("RegisterGenerated<global::Testing.OrderEvents.OrderCreatedEvent>();");
		await Assert.That(registerBody).Contains("RegisterGenerated<global::Testing.OrderEvents.TotalUpdatedEvent>();");
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.OrderEvents.ShippingAddressSetEvent>();");
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.OrderEvents.OrderConfirmedEvent>();");
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.OrderEvents.OrderCanceledEvent>();");
	}

	[Test]
	public async Task Generate_GivenEventWithMultipleParameterTypes_GeneratesCorrectProperties(
		CancellationToken cancellationToken
	)
	{
		// Arrange ÔÇö event with int, decimal, string, bool parameters
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ProductAggregate : AggregateBase
	{
		public string Name { get; private set; }
		public decimal Price { get; private set; }
		public int Quantity { get; private set; }
		public bool IsAvailable { get; private set; }

		[Event]
		public partial void UpdateProduct(string name, decimal price, int quantity, bool isAvailable);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var productUpdated = query.GetRecord("ProductUpdatedEvent", "Testing.ProductEvents");
		await Assert.That(productUpdated.HasProperty("Name", TypeRefs.String)).IsTrue();
		await Assert.That(productUpdated.HasProperty("Price", TypeRefs.Decimal)).IsTrue();
		await Assert.That(productUpdated.HasProperty("Quantity", TypeRefs.Int)).IsTrue();
		await Assert.That(productUpdated.HasProperty("IsAvailable", TypeRefs.Bool)).IsTrue();

		var aggregate = query.GetClass("ProductAggregate", "Testing");
		var apply = aggregate.GetMethod("Apply", TypeRefs.Event("ProductUpdatedEvent", "Testing.ProductEvents"));
		var applyBody = apply.Node.Body?.ToString() ?? string.Empty;
		await Assert.That(applyBody).Contains("Name = @event.Name;");
		await Assert.That(applyBody).Contains("Price = @event.Price;");
		await Assert.That(applyBody).Contains("Quantity = @event.Quantity;");
		await Assert.That(applyBody).Contains("IsAvailable = @event.IsAvailable;");

		// Equality/hash is compiler-generated for the record; no BuildEventHash override exists.
		await Assert.That(productUpdated.HasMethod("BuildEventHash", TypeRefs.HashCode)).IsFalse();
	}

	[Test]
	public async Task Generate_GivenAggregateWithTransitiveInheritance_GeneratesCode(
		CancellationToken cancellationToken
	)
	{
		// Arrange ÔÇö aggregate inherits through an intermediate base class
		const string source =
			@"
namespace Testing
{
	public abstract class DomainAggregateBase : AggregateBase
	{
	}

	[Aggregate]
	public partial class AccountAggregate : DomainAggregateBase
	{
		public string AccountName { get; private set; }

		[Event]
		public partial void CreateAccount(string accountName);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert that attribute files + 1 generated aggregate
		await Assert.That(result.DriverResult.GeneratedTrees).Count().IsEqualTo(ExpectedFileCountPlusGen);

		var query = result.Generated();
		await Assert.That(query.HasRecord("AccountCreatedEvent", "Testing.AccountEvents")).IsTrue();
		var registerBody =
			query.GetClass("AccountAggregate", "Testing").GetMethod("RegisterEvents").Node.Body?.ToString()
			?? string.Empty;
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.AccountEvents.AccountCreatedEvent>();");
	}

	[Test]
	public async Task Generate_GivenAggregateWithMultiLevelTransitiveInheritance_GeneratesCode(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	public abstract class DomainAggregateBase : AggregateBase
	{
	}

	public abstract class BillingAggregateBase : DomainAggregateBase
	{
	}

	[Aggregate]
	public partial class InvoiceAggregate : BillingAggregateBase
	{
		public string InvoiceNumber { get; private set; }

		[Event]
		public partial void CreateInvoice(string invoiceNumber);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result.DriverResult.GeneratedTrees).Count().IsEqualTo(ExpectedFileCountPlusGen);

		var query = result.Generated();
		await Assert.That(query.HasRecord("InvoiceCreatedEvent", "Testing.InvoiceEvents")).IsTrue();
		var registerBody =
			query.GetClass("InvoiceAggregate", "Testing").GetMethod("RegisterEvents").Node.Body?.ToString()
			?? string.Empty;
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.InvoiceEvents.InvoiceCreatedEvent>();");
		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.AggregateMustInheritAggregateBase);
	}

	[Test]
	public async Task Generate_GivenNestedNamespace_GeneratesCorrectEventsNamespace(CancellationToken cancellationToken)
	{
		// Arrange ÔÇö deeply nested namespace
		const string source =
			@"
namespace Company.Domain.Orders
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		// Assert that events namespace follows the pattern
		await Assert.That(query.HasNamespace("Company.Domain.Orders.OrderEvents")).IsTrue();
		await Assert.That(query.HasNamespace("Company.Domain.Orders")).IsTrue();
		var registerBody =
			query.GetClass("OrderAggregate", "Company.Domain.Orders").GetMethod("RegisterEvents").Node.Body?.ToString()
			?? string.Empty;
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Company.Domain.Orders.OrderEvents.OrderCreatedEvent>();");
	}

	[Test]
	public async Task Generate_GivenParameterlessAndParameterizedEvents_GeneratesBothCorrectly(
		CancellationToken cancellationToken
	)
	{
		// Arrange ÔÇö mix of parameterless and parameterized events
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class CounterAggregate : AggregateBase
	{
		public int Count { get; private set; }
		public string Label { get; private set; }

		[Event]
		public partial void Increment();

		[Event]
		public partial void Decrement();

		[Event]
		public partial void SetLabel(string label);

		[Event]
		public partial void Reset();

		partial void Apply(global::Testing.CounterEvents.IncrementedEvent @event) => Count++;
		partial void Apply(global::Testing.CounterEvents.DecrementedEvent @event) => Count--;
		partial void Apply(global::Testing.CounterEvents.ResetEvent @event) => Count = 0;
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("CounterAggregate", "Testing");

		// Assert that parameterless use () constructor, parameterized use { } initializer
		var incrementBody = aggregate.GetMethod("Increment").Node.Body?.ToString() ?? string.Empty;
		await Assert.That(incrementBody).Contains("var @event = new global::Testing.CounterEvents.IncrementedEvent");
		await Assert.That(incrementBody).Contains("RecordAndApply(@event);");

		var decrementBody = aggregate.GetMethod("Decrement").Node.Body?.ToString() ?? string.Empty;
		await Assert.That(decrementBody).Contains("var @event = new global::Testing.CounterEvents.DecrementedEvent");
		await Assert.That(decrementBody).Contains("RecordAndApply(@event);");

		var resetBody = aggregate.GetMethod("Reset").Node.Body?.ToString() ?? string.Empty;
		await Assert.That(resetBody).Contains("var @event = new global::Testing.CounterEvents.ResetEvent");
		await Assert.That(resetBody).Contains("RecordAndApply(@event);");

		var setLabelBody = aggregate.GetMethod("SetLabel", TypeRefs.String).Node.Body?.ToString() ?? string.Empty;
		await Assert.That(setLabelBody).Contains("Label = label,");

		// Assert that all 4 RegisterGenerated calls
		var registerBody = aggregate.GetMethod("RegisterEvents").Node.Body?.ToString() ?? string.Empty;
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.CounterEvents.IncrementedEvent>();");
		await Assert
			.That(registerBody)
			.Contains("RegisterGenerated<global::Testing.CounterEvents.DecrementedEvent>();");
		await Assert.That(registerBody).Contains("RegisterGenerated<global::Testing.CounterEvents.LabelSetEvent>();");
		await Assert.That(registerBody).Contains("RegisterGenerated<global::Testing.CounterEvents.ResetEvent>();");
	}

	[Test]
	public async Task Generate_GivenNullableParameter_GeneratesNullableProperty(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ProfileAggregate : AggregateBase
	{
		public string? Bio { get; private set; }

		[Event]
		public partial void UpdateBio(string? bio);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var bioUpdated = query.GetRecord("BioUpdatedEvent", "Testing.ProfileEvents");
		await Assert.That(bioUpdated.HasProperty("Bio", TypeRefs.String)).IsTrue();

		var aggregate = query.GetClass("ProfileAggregate", "Testing");
		var apply = aggregate.GetMethod("Apply", TypeRefs.Event("BioUpdatedEvent", "Testing.ProfileEvents"));
		await Assert.That(apply.Node.Body?.ToString()).Contains("Bio = @event.Bio;");

		var updateBio = aggregate.GetMethod("UpdateBio", TypeRefs.String);
		await Assert.That(updateBio.Node.Modifiers.ToString()).Contains("partial");
	}

	[Test]
	public async Task Generate_GivenNotNullParameter_GeneratesGuard(CancellationToken cancellationToken)
	{
		const string source = """
using System.Diagnostics.CodeAnalysis;

namespace Testing;

[Aggregate]
public partial class ProfileAggregate
{
	public string? Bio { get; private set; }

	[Event]
	public partial void UpdateBio([NotNull] string? bio);
}
""";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		await Assert
			.That(query.GetRecord("BioUpdatedEvent", "Testing.ProfileEvents").HasProperty("Bio", TypeRefs.String))
			.IsTrue();

		var aggregate = query.GetClass("ProfileAggregate", "Testing");
		var body = aggregate.GetMethod("UpdateBio", TypeRefs.String).Node.Body?.ToString() ?? string.Empty;
		await Assert.That(result).DoesNotHaveDiagnostic("CS8777");
		await Assert.That(body).Contains("var __bioValue = bio!;");
		await Assert.That(body).Contains("if (bio is null)");
		await Assert.That(body).Contains("throw new global::System.ArgumentNullException(nameof(bio));");
		await Assert.That(body).Contains("OnBioChanging(ref __bioValue);");
		await Assert.That(body).Contains("Bio = __bioValue!,");
	}

	[Test]
	public async Task Generate_GivenRequiredStringParameter_GeneratesGuard(CancellationToken cancellationToken)
	{
		const string source = """
using System.ComponentModel.DataAnnotations;

namespace Testing;

[Aggregate]
public partial class ProfileAggregate
{
	public string? Bio { get; private set; }

	[Event]
	public partial void UpdateBio([Required] string? bio);
}
""";

		var result = await GenerateAsync(source, cancellationToken);
		var cs8777 = result
			.CompilationResult.Compilation.GetDiagnostics(cancellationToken)
			.Where(d => d.Id == "CS8777")
			.ToArray();

		var query = result.Generated();
		await Assert
			.That(query.GetRecord("BioUpdatedEvent", "Testing.ProfileEvents").HasProperty("Bio", TypeRefs.String))
			.IsTrue();

		var body =
			query.GetClass("ProfileAggregate", "Testing").GetMethod("UpdateBio", TypeRefs.String).Node.Body?.ToString()
			?? string.Empty;
		await Assert.That(cs8777).IsEmpty();
		await Assert.That(body).Contains("var __bioValue = bio!;");
		await Assert.That(body).Contains("if (global::System.String.IsNullOrWhiteSpace(bio))");
		await Assert
			.That(body)
			.Contains(
				"throw new global::System.ArgumentException(\"Parameter 'bio' cannot be null or empty.\", nameof(bio));"
			);
		await Assert.That(body).Contains("OnBioChanging(ref __bioValue);");
		await Assert.That(body).Contains("Bio = __bioValue!,");
	}

	[Test]
	public async Task Generate_GivenPublicAccessibility_GeneratesPublicPartialClass(CancellationToken cancellationToken)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class PublicAggregate : AggregateBase
	{
		[Event]
		public partial void DoAction();
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		// Assert
		var publicAggregate = result.Generated().GetClass("PublicAggregate", "Testing");
		var modifiers = publicAggregate.Node.Modifiers.ToString();
		await Assert.That(modifiers).Contains("public");
		await Assert.That(modifiers).Contains("partial");
	}

	[Test]
	public async Task Generate_GivenEventWithSingleParameter_CommandMethodHasCorrectSignature(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class NoteAggregate : AggregateBase
	{
		public string Content { get; private set; }

		[Event]
		public partial void SetContent(string content);
	}
}
";

		// Act
		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		// Assert that command method matches partial declaration
		var setContent = query.GetClass("NoteAggregate", "Testing").GetMethod("SetContent", TypeRefs.String);
		await Assert.That(setContent.Node.Modifiers.ToString()).Contains("partial");
		// Assert that RecordAndApply creates event with property
		await Assert.That(setContent.Node.Body?.ToString()).Contains("Content = content,");
	}

	[Test]
	public async Task Generate_GivenGeneratedEvent_InvokesShouldApplyBeforeAndAfterOnRaising(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class CustomerAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = default!;

		[Event]
		public partial void SetCustomerId(string customerId);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("CustomerAggregate", "Testing");
		var setCustomerIdBody =
			aggregate.GetMethod("SetCustomerId", TypeRefs.String).Node.Body?.ToString() ?? string.Empty;

		var firstShouldApplyIndex = setCustomerIdBody.IndexOf(
			"if (!ShouldApplyCustomerIdSetEvent(@event))",
			StringComparison.Ordinal
		);
		var onRaisingIndex = setCustomerIdBody.IndexOf(
			"OnRaisingCustomerIdSetEvent(ref customerId);",
			StringComparison.Ordinal
		);
		var secondShouldApplyIndex = setCustomerIdBody.IndexOf(
			"if (!ShouldApplyCustomerIdSetEvent(@event))",
			firstShouldApplyIndex + 1,
			StringComparison.Ordinal
		);

		await Assert.That(firstShouldApplyIndex).IsGreaterThanOrEqualTo(0);
		await Assert.That(onRaisingIndex).IsGreaterThanOrEqualTo(0);
		await Assert.That(secondShouldApplyIndex).IsGreaterThanOrEqualTo(0);
		await Assert.That(firstShouldApplyIndex).IsLessThan(onRaisingIndex);
		await Assert.That(onRaisingIndex).IsLessThan(secondShouldApplyIndex);

		var onShouldApply = aggregate.GetMethod(
			"OnShouldApplyCustomerIdSetEvent",
			TypeRefs.Event("CustomerIdSetEvent", "Testing.CustomerEvents"),
			TypeRefs.Bool
		);
		await Assert.That(onShouldApply.Node.Modifiers.ToString()).Contains("partial");
	}

	[Test]
	public async Task Generate_GivenCommandMethod_AllocatesSingleEventInstanceAndSyncsAfterRaisingHook(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class CustomerAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = default!;

		[Event]
		public partial void SetCustomerId(string customerId);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("CustomerAggregate", "Testing");
		var setCustomerIdBody =
			aggregate.GetMethod("SetCustomerId", TypeRefs.String).Node.Body?.ToString() ?? string.Empty;

		// The command method must allocate exactly one event instance, then sync the post-hook
		// values into it before re-evaluating ShouldApply.
		const string eventCreationMarker = "var @event = new ";
		var eventCreationCount = 0;
		var searchIndex = 0;
		while (
			(searchIndex = setCustomerIdBody.IndexOf(eventCreationMarker, searchIndex, StringComparison.Ordinal)) >= 0
		)
		{
			eventCreationCount++;
			searchIndex += eventCreationMarker.Length;
		}
		await Assert.That(eventCreationCount).IsEqualTo(1);

		var onRaisingIndex = setCustomerIdBody.IndexOf(
			"OnRaisingCustomerIdSetEvent(ref customerId);",
			StringComparison.Ordinal
		);
		var syncIndex = setCustomerIdBody.IndexOf("@event.CustomerId = customerId;", StringComparison.Ordinal);
		var secondShouldApplyIndex = setCustomerIdBody.IndexOf(
			"if (!ShouldApplyCustomerIdSetEvent(@event))",
			onRaisingIndex + 1,
			StringComparison.Ordinal
		);

		await Assert.That(onRaisingIndex).IsGreaterThanOrEqualTo(0);
		await Assert.That(onRaisingIndex).IsLessThan(syncIndex);
		await Assert.That(syncIndex).IsLessThan(secondShouldApplyIndex);
	}

	[Test]
	public async Task Generate_GivenSecondShouldApplyReturnsFalse_StopsProcessingAndReturnsToCaller(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class CustomerAggregate : AggregateBase
	{
		public int ShouldApplyCallCount { get; private set; }
		public string? LastRaisedValue { get; private set; }
		public string? CustomerId { get; private set; }

		[Event]
		public partial bool SetCustomerId(string customerId);

		partial void OnRaisingCustomerIdSetEvent(ref string customerId)
		{
			customerId = ""raised-value"";
			LastRaisedValue = customerId;
		}

		partial void OnShouldApplyCustomerIdSetEvent(global::Testing.CustomerEvents.CustomerIdSetEvent @event, ref bool shouldApply)
		{
			ShouldApplyCallCount++;
			shouldApply = ShouldApplyCallCount == 1;
		}
	}
}
";

		var result = await GenerateAsync(
			source,
			EventSourcingGeneratorTestOptions.Default.Compile(),
			cancellationToken
		);
		var assembly = await Assert.That(result.CompilationResult.Assembly).IsNotNull();

		var aggregateType = assembly.GetType("Testing.CustomerAggregate")!;
		var instance = Activator.CreateInstance(aggregateType)!;
		var setCustomerId = aggregateType.GetMethod("SetCustomerId")!;

		var setResult = (bool)setCustomerId.Invoke(instance, ["input-value"])!;

		await Assert.That(setResult).IsFalse();
		await Assert.That(aggregateType.GetProperty("ShouldApplyCallCount")!.GetValue(instance)).IsEqualTo(2);
		await Assert.That(aggregateType.GetProperty("LastRaisedValue")!.GetValue(instance)).IsEqualTo("raised-value");
		await Assert.That(aggregateType.GetProperty("CustomerId")!.GetValue(instance)).IsNull();
	}

	[Test]
	public async Task Generate_GivenNullValidationHook_RunsValidationBeforeNoChangeGuard(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class CustomerAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = default!;

		[Event]
		public partial void SetCustomerId(string customerId);

		partial void OnCustomerIdChanging(ref string customerId) => global::System.ArgumentNullException.ThrowIfNull(customerId);
	}
}
";

		var result = await GenerateAsync(
			source,
			EventSourcingGeneratorTestOptions.Default.Compile(),
			cancellationToken
		);
		var query = result.Generated();
		var setCustomerIdBody =
			query
				.GetClass("CustomerAggregate", "Testing")
				.GetMethod("SetCustomerId", TypeRefs.String)
				.Node.Body?.ToString()
			?? string.Empty;

		var onChangingIndex = setCustomerIdBody.IndexOf(
			"OnCustomerIdChanging(ref customerId);",
			StringComparison.Ordinal
		);
		var onRaisingIndex = setCustomerIdBody.IndexOf(
			"OnRaisingCustomerIdSetEvent(ref customerId);",
			StringComparison.Ordinal
		);
		var noChangeIndex = setCustomerIdBody.IndexOf(
			"if (global::System.String.Equals(CustomerId, customerId, global::System.StringComparison.Ordinal))",
			StringComparison.Ordinal
		);

		await Assert.That(onChangingIndex).IsGreaterThanOrEqualTo(0);
		await Assert.That(onRaisingIndex).IsGreaterThanOrEqualTo(0);
		await Assert.That(noChangeIndex).IsGreaterThanOrEqualTo(0);
		await Assert.That(onChangingIndex).IsLessThan(noChangeIndex);
		await Assert.That(onRaisingIndex).IsLessThan(noChangeIndex);

		var assembly = await Assert.That(result.CompilationResult.Assembly).IsNotNull();

		var aggregateType = assembly.GetType("Testing.CustomerAggregate")!;
		var instance = Activator.CreateInstance(aggregateType)!;
		var setCustomerId = aggregateType.GetMethod("SetCustomerId")!;
		var threwArgumentNullException = false;

		try
		{
			setCustomerId.Invoke(instance, [null]);
		}
		catch (TargetInvocationException ex) when (ex.InnerException is ArgumentNullException)
		{
			threwArgumentNullException = true;
		}

		await Assert.That(threwArgumentNullException).IsTrue();
	}

	[Test]
	public async Task Generate_GivenSimpleAggregate_CanRoundTripPrivateSetterStateWithSystemTextJson(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = string.Empty;
		public decimal Total { get; private set; }

		[Event]
		public partial void CreateOrder(string customerId, decimal total);
	}
}
";

		var result = await GenerateAsync(
			source,
			EventSourcingGeneratorTestOptions.Default.Compile(),
			cancellationToken
		);
		var assembly = await Assert.That(result.CompilationResult.Assembly).IsNotNull();

		var aggregateType = assembly.GetType("Testing.OrderAggregate")!;
		var instance = Activator.CreateInstance(aggregateType)!;
		aggregateType.GetMethod("CreateOrder")!.Invoke(instance, ["customer-1", 12.5m]);

		var detailsProperty = aggregateType.GetProperty("Details")!;
		var detailsType = detailsProperty.PropertyType;
		var details = Activator.CreateInstance(detailsType)!;
		detailsType.GetProperty("Id")!.SetValue(details, "aggregate-1");
		detailsProperty.SetValue(instance, details);

		var json = JsonSerializer.Serialize(instance, aggregateType);
		var roundTripped = JsonSerializer.Deserialize(json, aggregateType)!;

		await Assert.That(aggregateType.GetProperty("CustomerId")!.GetValue(roundTripped)).IsEqualTo("customer-1");
		await Assert.That(aggregateType.GetProperty("Total")!.GetValue(roundTripped)).IsEqualTo(12.5m);
		var roundTrippedDetails = detailsProperty.GetValue(roundTripped)!;
		await Assert.That(detailsType.GetProperty("Id")!.GetValue(roundTrippedDetails)).IsEqualTo("aggregate-1");
	}

	[Test]
	public async Task Generate_GivenGeneratedEvent_CanRoundTripPayloadPropertiesWithSystemTextJson(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class OrderAggregate : AggregateBase
	{
		public string CustomerId { get; private set; } = string.Empty;

		[Event]
		public partial void CreateOrder(string customerId);
	}
}
";

		var result = await GenerateAsync(
			source,
			EventSourcingGeneratorTestOptions.Default.Compile(),
			cancellationToken
		);
		var assembly = await Assert.That(result.CompilationResult.Assembly).IsNotNull();

		var eventType = assembly.GetType("Testing.OrderEvents.OrderCreatedEvent")!;
		var instance = Activator.CreateInstance(eventType)!;
		eventType.GetProperty("CustomerId")!.SetValue(instance, "customer-2");
		var metadataProperty = eventType.GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance)!;
		var metadataType = metadataProperty.PropertyType;
		var metadata = Activator.CreateInstance(metadataType)!;
		metadataType.GetProperty("CorrelationId")!.SetValue(metadata, "corr-1");
		metadataProperty.SetValue(instance, metadata);

		var json = JsonSerializer.Serialize(instance, eventType);

		// Metadata is framework-managed and excluded from the payload JSON.
		await Assert.That(json).DoesNotContain("Metadata");

		var roundTripped = JsonSerializer.Deserialize(json, eventType)!;
		await Assert.That(eventType.GetProperty("CustomerId")!.GetValue(roundTripped)).IsEqualTo("customer-2");
	}

	[Test]
	public async Task Generate_GivenSuspiciousMethodNames_ReportsVerbPhraseDiagnostics(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class NamingAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;

		[Event(EventName = ""CustomerRegistered"")]
		public partial void NewCustomer(string value);

		[Event(EventName = ""CustomerCreated"")]
		public partial void CustomerRegistered(string value);

		[Event(EventName = ""ValueChanged"")]
		public partial void NameChanged(string value);

		[Event(EventName = ""ValueSet"")]
		public partial void Handle(string value);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostics(DiagnosticLibrary.AggregateMethodShouldBeVerbPhrase.Id, 4);
		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnableToInferEventName);
		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.EventNameOverrideShouldBePastTense);
	}

	[Test]
	public async Task Generate_GivenInvalidEventNameOverrides_ReportsPastTenseDiagnostics(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class NamingAggregate : AggregateBase
	{
		public string Value { get; private set; } = default!;

		[Event(EventName = ""RegisterCustomer"")]
		public partial void NewCustomer(string value);

		[Event(EventName = ""CreateCustomer"")]
		public partial void CreateCustomer(string value);

		[Event(EventName = ""ApproveQuestion"")]
		public partial void ApproveQuestion(string value);

		[Event(EventName = ""WithdrawConsent"")]
		public partial void WithdrawConsent(string value);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostics(DiagnosticLibrary.EventNameOverrideShouldBePastTense, 4);
	}

	[Test]
	public async Task Generate_GivenManualEventTypes_ReportsPastTenseDiagnostics(CancellationToken cancellationToken)
	{
		const string source =
			@"
namespace Testing
{
	[Events.EventContract]
	public sealed record NameChanged
	{
		public static int SchemaVersion => 1;
	}

	[Events.EventContract]
	public sealed record ChangeName
	{
		public static int SchemaVersion => 1;
	}

	[Events.EventContract]
	public sealed record CustomerRegisteredEvent
	{
		public static int SchemaVersion => 1;
	}

}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.EventNameOverrideShouldBePastTense);
		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnableToInferEventName);
	}

	[Test]
	public async Task Generate_GivenNonNullableParamMappedToNullableProperty_EmitsNullabilityMismatchWarning(
		CancellationToken cancellationToken
	)
	{
		// Non-nullable string parameter mapping to a nullable string? aggregate property.
		// The generator handles it automatically (adds a local cast), but also emits EVENTSTORE016
		// to guide the developer to fix the signature rather than rely on the workaround.
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class NoteAggregate : AggregateBase
	{
			public string? Note { get; private set; }

			[Event]
			public partial void SetNote(string note);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);
		var warnings = result
			.DriverResult.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Info)
			.ToArray();

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.EventParameterNullabilityMismatch);
	}

	[Test]
	public async Task Generate_GivenNullableParamMappedToNullableProperty_DoesNotEmitNullabilityMismatchWarning(
		CancellationToken cancellationToken
	)
	{
		// When the parameter already has the matching nullable annotation, no warning should be emitted.
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class NoteAggregate : AggregateBase
	{
			public string? Note { get; private set; }

			[Event]
			public partial void SetNote(string? note);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.EventParameterNullabilityMismatch);
	}

	[Test]
	[Arguments("System.Collections.Generic.List<string>")]
	[Arguments("System.Collections.Generic.IList<string>")]
	[Arguments("System.Collections.Generic.ICollection<string>")]
	[Arguments("System.Collections.Generic.IReadOnlyList<string>")]
	[Arguments("System.Collections.Generic.IReadOnlyCollection<string>")]
	[Arguments("System.Collections.Generic.IEnumerable<string>")]
	[Arguments("System.Collections.Generic.HashSet<string>")]
	[Arguments("string[]")]
	public async Task Generate_GivenNonEventStoreCollectionProperty_ReportsCollectionTypeError(
		string collectionType,
		CancellationToken cancellationToken
	)
	{
		var source =
			$@"namespace Testing;

[Aggregate]
public partial class ItemAggregate : AggregateBase
{{
	public {collectionType} Tags {{ get; private set; }}

	[Event]
	public partial void SetTags({collectionType} tags);
}}
";

		var result = await GenerateAsync(source, EventSourcingGeneratorTestOptions.NoValidation, cancellationToken);

		await Assert
			.That(result)
			.HasDiagnostic(DiagnosticLibrary.AggregatePropertyCollectionTypeMustUseEventStoreCollections);
	}

	[Test]
	[Arguments("EventStoreList<string>")]
	[Arguments("EventStoreSet<string>")]
	public async Task Generate_GivenEventStoreCollectionProperty_DoesNotReportCollectionTypeError(
		string collectionType,
		CancellationToken cancellationToken
	)
	{
		var source =
			$@"namespace Testing;

[Aggregate]
public partial class ItemAggregate : AggregateBase
{{
	public {collectionType} Tags {{ get; private set; }} = new();

	[Event]
	public partial void SetTags({collectionType} tags);
}}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert
			.That(result)
			.DoesNotHaveDiagnostic(DiagnosticLibrary.AggregatePropertyCollectionTypeMustUseEventStoreCollections);
	}

	[Test]
	public async Task Generate_GivenCollectionEvents_UsesCollectionSemanticsAndSharedEnumerableHooks(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ItemAggregate : AggregateBase
	{
		public EventStoreSet<string> Tags { get; private set; } = [];

		[CollectionEvent(nameof(Tags))]
		public partial ItemAggregate AddTag(string tag);

		[CollectionEvent(nameof(Tags))]
		public partial ItemAggregate AddTags(System.Collections.Generic.IEnumerable<string> tags);

		[CollectionEvent(nameof(Tags))]
		public partial ItemAggregate AddTags(params string[] tags);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnsupportedEventMethodSignature);

		var query = result.Generated();
		var aggregate = query.GetClass("ItemAggregate", "Testing");
		var enumerableTags = TypeRefs.EnumerableOf(TypeRefs.String);
		var onNormalizing = aggregate.GetMethod("OnNormalizingAddTags", enumerableTags);
		await Assert.That(onNormalizing.Node.Modifiers.ToString()).Contains("partial");
		await Assert.That(onNormalizing.Node.ParameterList.Parameters[0].Modifiers.ToString()).Contains("ref");

		var onValidating = aggregate.GetMethod("OnValidatingAddTags", enumerableTags);
		await Assert.That(onValidating.Node.Modifiers.ToString()).Contains("partial");

		var addTagBody = aggregate.GetMethod("AddTag", TypeRefs.String).Node.Body?.ToString() ?? string.Empty;
		await Assert.That(addTagBody).Contains("if (Tags.Contains(__itemValue))");

		var addTagsBody = aggregate.GetMethod("AddTags", enumerableTags).Node.Body?.ToString() ?? string.Empty;
		await Assert.That(addTagsBody).Contains("var __eventItems = __itemsValue as string[] ?? [.. __itemsValue];");

		var applyTagsAdded = aggregate.GetMethod("Apply", TypeRefs.Event("TagsAddedEvent", "Testing.ItemEvents"));
		await Assert
			.That(applyTagsAdded.Node.Body?.ToString())
			.Contains("((global::System.Collections.Generic.ICollection<string>)Tags).Add(__item);");
	}

	[Test]
	public async Task Generate_GivenManualEventAttribute_DisablesAutomaticApplyAndRequiresImplementation(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing;

[Aggregate]
public partial class ManualAggregate
{
	public string Value { get; private set; } = string.Empty;

	[Event(EventName = ""ValueCommandAppliedEvent"", Manual = true)]
	public partial void ApplyValueCommand(string input);

	private partial void Apply(global::Testing.ManualEvents.ValueCommandAppliedEvent @event)
	{
		Value = @event.Input;
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("ManualAggregate", "Testing");
		var apply = aggregate.GetMethod("Apply", TypeRefs.Event("ValueCommandAppliedEvent", "Testing.ManualEvents"));
		var modifiers = apply.Node.Modifiers.ToString();
		await Assert.That(modifiers).Contains("partial");
		await Assert.That(modifiers).Contains("private");
		// The Apply method must remain a declaration only; no generated body.
		await Assert.That(apply.Node.Body).IsNull();
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task Generate_GivenManualCollectionEventAttribute_DisablesAutomaticApplyAndRequiresImplementation(
		bool useExpressionMethod,
		CancellationToken cancellationToken
	)
	{
		const string methodBody = "((global::System.Collections.Generic.ICollection<string>)Tags).Add(@event.Tag);";
		const string expressionMethodSource = @$"=> {methodBody}";
		const string blockMethodSource =
			@$"
{{
	{methodBody}
}}
";

		var source =
			@$"
namespace Testing;

[Aggregate]
public sealed partial class ManualCollectionAggregate
{{
	public EventStoreList<string> Tags {{ get; private set; }} = [];

	[CollectionEvent(nameof(Tags), Manual = true)]
	public partial void AddTag(string tag);

	private partial void Apply(global::Testing.ManualCollectionEvents.TagAddedEvent @event)
	{(useExpressionMethod ? expressionMethodSource : blockMethodSource)}
}}
";

		var result = await GenerateAsync(source, cancellationToken);

		var query = result.Generated();
		var aggregate = query.GetClass("ManualCollectionAggregate", "Testing");
		var apply = aggregate.GetMethod("Apply", TypeRefs.Event("TagAddedEvent", "Testing.ManualCollectionEvents"));
		var modifiers = apply.Node.Modifiers.ToString();
		await Assert.That(modifiers).Contains("partial");
		await Assert.That(modifiers).Contains("private");
		// The Apply method must remain a declaration only; no generated body.
		await Assert.That(apply.Node.Body).IsNull();
	}

	[Test]
	public async Task Generate_GivenCollectionRemoveMethodName_InfersRemoveMutationAndSkipsNoChangeEvents(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ItemAggregate : AggregateBase
	{
		public EventStoreSet<string> Tags { get; private set; } = [];

		[CollectionEvent(nameof(Tags))]
		public partial ItemAggregate RemoveTag(string tag);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnsupportedEventMethodSignature);

		var query = result.Generated();
		await Assert.That(query.HasClass("ItemAggregate", "Testing")).IsTrue();
		var aggregate = query.GetClass("ItemAggregate", "Testing");

		var removeTagBody = aggregate.GetMethod("RemoveTag", TypeRefs.String).Node.Body?.ToString() ?? string.Empty;
		await Assert.That(removeTagBody).Contains("if (!Tags.Contains(__itemValue))");

		var apply = aggregate.GetMethod("Apply", TypeRefs.Event("TagRemovedEvent", "Testing.ItemEvents"));
		await Assert
			.That(apply.Node.Body?.ToString())
			.Contains("((global::System.Collections.Generic.ICollection<string>)Tags).Remove(@event.Tag);");
	}

	[Test]
	public async Task Generate_GivenCollectionOperationOverride_UsesSpecifiedMutation(
		CancellationToken cancellationToken
	)
	{
		const string source =
			@"
namespace Testing
{
	[Aggregate]
	public partial class ItemAggregate : AggregateBase
	{
		public EventStoreSet<string> Tags { get; private set; } = [];

		[CollectionEvent(nameof(Tags), Operation = CollectionEventOperation.Remove)]
		public partial ItemAggregate ArchiveTag(string tag);

		[CollectionEvent(nameof(Tags), Operation = CollectionEventOperation.Add)]
		public partial ItemAggregate DeleteTag(string tag);
	}
}
";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnsupportedEventMethodSignature);

		var query = result.Generated();
		var aggregate = query.GetClass("ItemAggregate", "Testing");
		var archiveTagBody = aggregate.GetMethod("ArchiveTag", TypeRefs.String).Node.Body?.ToString() ?? string.Empty;
		await Assert.That(archiveTagBody).Contains("if (!Tags.Contains(__itemValue))");

		var deleteTagBody = aggregate.GetMethod("DeleteTag", TypeRefs.String).Node.Body?.ToString() ?? string.Empty;
		await Assert.That(deleteTagBody).Contains("if (Tags.Contains(__itemValue))");

		var archiveApply = aggregate.GetMethod("Apply", TypeRefs.Event("TagArchivedEvent", "Testing.ItemEvents"));
		await Assert
			.That(archiveApply.Node.Body?.ToString())
			.Contains("((global::System.Collections.Generic.ICollection<string>)Tags).Remove(@event.Tag);");

		var deleteApply = aggregate.GetMethod("Apply", TypeRefs.Event("TagDeletedEvent", "Testing.ItemEvents"));
		await Assert
			.That(deleteApply.Node.Body?.ToString())
			.Contains("((global::System.Collections.Generic.ICollection<string>)Tags).Add(@event.Tag);");
	}
}
