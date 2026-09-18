using Purview.EventSourcing.SourceGenerator.Analyzers;

namespace Purview.EventSourcing.SourceGenerator.CodeFixes;

public sealed class AddPartialModifierCodeFixTests
{
	const string NonPartialAggregate = """
		using Purview.EventSourcing.Aggregates;

		namespace Testing
		{
			[Aggregate]
			public class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }
			}
		}
		""";

	const string NonPartialEventMethod = """
		using Purview.EventSourcing.Aggregates;

		namespace Testing
		{
			[Aggregate]
			public partial class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }

				[Event]
				public void CreateOrder(string customerId);
			}
		}
		""";

	const string NestedNonPartialAggregate = """
		using Purview.EventSourcing.Aggregates;

		namespace Testing
		{
			public class Outer
			{
				[Aggregate]
				public class OrderAggregate : AggregateBase
				{
					public string CustomerId { get; private set; }
				}
			}
		}
		""";

	const string GenericNonPartialAggregate = """
		using Purview.EventSourcing.Aggregates;

		namespace Testing
		{
			[Aggregate]
			public class OrderAggregate<T> : AggregateBase
			{
				public string CustomerId { get; private set; }
			}
		}
		""";

	const string TwoNonPartialAggregates = """
		using Purview.EventSourcing.Aggregates;

		namespace Testing
		{
			[Aggregate]
			public class OrderAggregate : AggregateBase
			{
				public string CustomerId { get; private set; }
			}

			[Aggregate]
			public class CustomerAggregate : AggregateBase
			{
				public string Name { get; private set; }
			}
		}
		""";

	[Test]
	public async Task GivenNonPartialAggregate_AddsPartialModifier(CancellationToken cancellationToken)
	{
		var result = await CodeFixTestHarness.ApplyAsync<
			AggregateDiagnosticAnalyzer,
			AddPartialModifierCodeFixProvider
		>(NonPartialAggregate, cancellationToken);

		await Assert.That(result.FixedCode).Contains("public partial class OrderAggregate");
	}

	[Test]
	public async Task GivenNonPartialEventMethod_AddsPartialModifier(CancellationToken cancellationToken)
	{
		var result = await CodeFixTestHarness.ApplyAsync<
			AggregateDiagnosticAnalyzer,
			AddPartialModifierCodeFixProvider
		>(NonPartialEventMethod, cancellationToken);

		await Assert.That(result.FixedCode).Contains("public partial void CreateOrder");
	}

	[Test]
	public async Task GivenNestedNonPartialAggregate_AddsPartialModifierPreservingNesting(
		CancellationToken cancellationToken
	)
	{
		var result = await CodeFixTestHarness.ApplyAsync<
			AggregateDiagnosticAnalyzer,
			AddPartialModifierCodeFixProvider
		>(NestedNonPartialAggregate, cancellationToken);

		await Assert.That(result.FixedCode).Contains("public partial class OrderAggregate");
		await Assert.That(result.FixedCode).Contains("public class Outer");
	}

	[Test]
	public async Task GivenGenericNonPartialAggregate_AddsPartialModifierPreservingTypeParameters(
		CancellationToken cancellationToken
	)
	{
		var result = await CodeFixTestHarness.ApplyAsync<
			AggregateDiagnosticAnalyzer,
			AddPartialModifierCodeFixProvider
		>(GenericNonPartialAggregate, cancellationToken);

		await Assert.That(result.FixedCode).Contains("public partial class OrderAggregate<T>");
	}

	[Test]
	public async Task GivenMultipleNonPartialAggregates_FixAllAddsPartialToEach(CancellationToken cancellationToken)
	{
		var result = await CodeFixTestHarness.ApplyFixAllAsync<
			AggregateDiagnosticAnalyzer,
			AddPartialModifierCodeFixProvider
		>(TwoNonPartialAggregates, cancellationToken);

		await Assert.That(result.FixedCode).Contains("public partial class OrderAggregate");
		await Assert.That(result.FixedCode).Contains("public partial class CustomerAggregate");
	}
}
