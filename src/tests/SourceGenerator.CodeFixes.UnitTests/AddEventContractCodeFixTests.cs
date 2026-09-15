using Purview.EventSourcing.SourceGenerator.Analyzers;

namespace Purview.EventSourcing.SourceGenerator.CodeFixes;

public sealed class AddEventContractCodeFixTests
{
	const string Source = """
		#nullable enable
		namespace Purview.EventSourcing.Aggregates.Events
		{
			[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
			public sealed class EventContractAttribute : System.Attribute { }

			public sealed class AggregateDetails { }
			public interface IAggregate { }

			public abstract class AggregateBase
			{
				public AggregateDetails Details { get; init; } = new();
				protected abstract void RegisterEvents();
				protected void Register<TEvent>(System.Action<TEvent> applier) where TEvent : class { }
			}
		}
		namespace Testing
		{
			public class OrderCreatedEvent { }

			public class OrderAggregate : Purview.EventSourcing.Aggregates.AggregateBase
			{
				protected override void RegisterEvents()
				{
					Register<OrderCreatedEvent>(_ => { });
				}
			}
		}
		""";

	[Test]
	public async Task GivenUnmarkedEventType_AddsEventContractAttribute(CancellationToken cancellationToken)
	{
		var result = await CodeFixTestHarness.ApplyAsync<EventStoreAnalyzer, AddEventContractCodeFixProvider>(
			Source,
			cancellationToken
		);

		await Assert.That(result.FixedCode).Contains("[global::Purview.EventSourcing.Aggregates.Events.EventContract]");
	}
}
