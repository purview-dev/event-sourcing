using Purview.EventSourcing.EntityFrameworkCore.SourceGenerator.Heleprs;

namespace Purview.EventSourcing.EntityFrameworkCore.SourceGenerator;

public sealed class EFSnapshotShapeAnalyzerTests
	: TUnitSourceGeneratorTestBase<EFSourceGenerator, EFSourceGeneratorTestOptions>
{
	[Test]
	public async Task Analyze_GivenDictionaryInAggregateGraph_RecommendsOpaqueOrEntryCollection(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public AssetDetails Details { get; } = new();
}

sealed class AssetDetails
{
	public IReadOnlyDictionary<string, int> OperatingSystemDistribution { get; } = new Dictionary<string, int>();
}
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.UnsupportedDictionary);
	}

	[Test]
	public async Task Analyze_GivenOpaqueDictionary_AllowsSnapshotShape(CancellationToken cancellationToken)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public AssetDetails Details { get; } = new();
}

sealed class AssetDetails
{
	[EFOpaque]
	public IReadOnlyDictionary<string, int> Values { get; } = new Dictionary<string, int>();
}
""";
		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnsupportedDictionary);
	}

	[Test]
	public async Task Analyze_GivenOpaquePropertyInSnapshotQuery_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	[EFOpaque]
	public IReadOnlyDictionary<string, int> Values { get; } = new Dictionary<string, int>();
}

static class Store
{
	public static void QueryAsync(System.Func<ReportAggregate, bool> predicate) { }
}

static class Consumer
{
	public static void Query() => Store.QueryAsync(report => report.Values.Count > 0);
}
""";
		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.OpaqueQuery);
	}

	[Test]
	public async Task Analyze_GivenValueTypeWithComplexConstructorParameter_ReportsDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public Capture Captured { get; set; }
}

readonly record struct Capture(UserInfo User, System.DateTimeOffset OccurredAt);

readonly record struct UserInfo(System.Guid Id, string? DisplayName);
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.UnconstructibleSnapshotType);
	}

	[Test]
	public async Task Analyze_GivenValueTypeWithParameterlessConstructor_AllowsSnapshotShape(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public Capture Captured { get; set; }
}

readonly record struct Capture(UserInfo User, System.DateTimeOffset OccurredAt)
{
	public Capture()
		: this(default!, default) { }
}

readonly record struct UserInfo(System.Guid Id, string? DisplayName);
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnconstructibleSnapshotType);
	}

	[Test]
	public async Task Analyze_GivenValueTypeWithScalarConstructorParameters_AllowsSnapshotShape(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public Amount Total { get; set; }
}

readonly record struct Amount(decimal Value);
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnconstructibleSnapshotType);
	}

	[Test]
	public async Task Analyze_GivenReferenceTypeWithComplexConstructorParameter_ReportsDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public Wrapper Wrapped { get; set; }
}

sealed class Wrapper
{
	public Wrapper(Info info) => Info = info;

	public Info Info { get; }
}

sealed class Info
{
	public string? Name { get; set; }
}
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.UnconstructibleSnapshotType);
	}

	[Test]
	public async Task Analyze_GivenNestedValueTypeWithoutUsableConstructor_ReportsDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public Holder Held { get; set; }
}

readonly record struct Holder(Inner Value)
{
	public Holder()
		: this(default!) { }
}

readonly record struct Inner(Details Details);

readonly record struct Details(string Name);
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.UnconstructibleSnapshotType);
	}

	[Test]
	public async Task Analyze_GivenScalarValueObjectWithComplexConstructor_DoesNotRequireEfConstructor(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Purview.ValueObjects.Serialization
{
	[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
	sealed class ScalarAttribute : System.Attribute;
}

namespace Testing
{
	[Aggregate]
	sealed class ReportAggregate
	{
		public Money Amount { get; set; }
	}

	[Purview.ValueObjects.Serialization.Scalar]
	sealed class Money
	{
		public Money(Details details) => Details = details;

		public Details Details { get; }
	}

	sealed class Details
	{
		public string? Name { get; set; }
	}
}
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnconstructibleSnapshotType);
	}

	[Test]
	public async Task Analyze_GivenValueObjectStructCollectionElement_DoesNotRequireEfConstructor(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Purview.ValueObjects.Serialization
{
	[System.AttributeUsage(System.AttributeTargets.Struct)]
	sealed class ValueObjectAttribute : System.Attribute;
}

namespace Purview.EventSourcing
{
	public sealed class EventStoreSet<T> : System.Collections.Generic.List<T>;
}

namespace Testing
{
	[Aggregate]
	sealed class ReportAggregate
	{
		public Purview.EventSourcing.EventStoreSet<Wrapper> Wrapped { get; set; } = [];
	}

	[Purview.ValueObjects.Serialization.ValueObject]
	readonly record struct Wrapper(Details Details);

	sealed class Details
	{
		public string? Name { get; set; }
	}
}
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnconstructibleSnapshotType);
	}

	[Test]
	public async Task Analyze_GivenReadOnlyComplexMemberOnValueType_ReportsDiagnostic(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public MirrorHolder Holder { get; set; } = new();
}

readonly struct MirrorHolder
{
	MirrorHolder(Info mirror) => Mirror = mirror;

	public MirrorHolder()
		: this(new Info()) { }

	public Info Mirror { get; }
}

sealed class Info
{
	public string? Name { get; set; }
}
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).HasDiagnostic(DiagnosticLibrary.NonWritableValueTypeMember);
	}

	[Test]
	public async Task Analyze_GivenSettableComplexMemberOnValueType_AllowsSnapshotShape(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public MirrorHolder Holder { get; set; } = new();
}

readonly struct MirrorHolder
{
	public Info Mirror { get; init; }
}

sealed class Info
{
	public string? Name { get; set; }
}
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.NonWritableValueTypeMember);
		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.UnconstructibleSnapshotType);
	}

	[Test]
	public async Task Analyze_GivenReadOnlyComplexMemberOnReferenceType_AllowsSnapshotShape(
		CancellationToken cancellationToken
	)
	{
		const string source = """
namespace Testing;

[Aggregate]
sealed class ReportAggregate
{
	public MirrorHolder Holder { get; set; }
}

sealed class MirrorHolder
{
	public MirrorHolder(Info mirror) => Mirror = mirror;

	public Info Mirror { get; }
}

sealed class Info
{
	public string? Name { get; set; }
}
""";

		var result = await GenerateAsync(source, cancellationToken);

		await Assert.That(result).DoesNotHaveDiagnostic(DiagnosticLibrary.NonWritableValueTypeMember);
	}

	protected override EFSourceGeneratorTestOptions OnBeforeRun(
		IEnumerable<string> sources,
		EFSourceGeneratorTestOptions options,
		CancellationToken cancellationToken
	) =>
		base.OnBeforeRun(
			sources,
			options with
			{
				DefaultNamespaces = options.DefaultNamespaces.Add(TypeLibrary.EFOpaqueAttribute.Namespace!),
				AnalyzerTypes = [typeof(EFSnapshotShapeAnalyzer)],
			},
			cancellationToken
		);
}
