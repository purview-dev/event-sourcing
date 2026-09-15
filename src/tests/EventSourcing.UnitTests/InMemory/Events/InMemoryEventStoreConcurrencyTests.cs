using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.InMemory.Events;

public sealed class InMemoryEventStoreConcurrencyTests
{
	[EventContract]
	sealed record ValueIncremented
	{
		public static int SchemaVersion => 1;

		public EventMetadata Metadata { get; init; }

		public int Amount { get; set; }
	}

	sealed class TestAggregate : AggregateBase
	{
		public int Value { get; private set; }

		protected override void RegisterEvents() => Register<ValueIncremented>(ev => Value += ev.Amount);

		public void Increment(int amount) => RecordAndApply(new ValueIncremented { Amount = amount });
	}

	static ServiceProvider CreateProvider()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddEventSourcing().AddInMemoryEventStore();
		return services.BuildServiceProvider();
	}

	static EventStoreOperationContext CreateContext() => new() { RequiresValidPrincipalIdentifier = false };

	[Test]
	public async Task SaveAsync_GivenConflictingAggregateVersion_ThrowsConcurrencyException()
	{
		// Arrange
		using var provider = CreateProvider();
		var eventStore = provider.GetRequiredService<IEventStore>();
		var context = CreateContext();

		// Writer A persists versions 1 and 2.
		var writerA = await eventStore.CreateAsync<TestAggregate>("agg-1");
		writerA.Increment(1);
		await eventStore.SaveAsync(writerA, context);

		writerA.Increment(2);
		await eventStore.SaveAsync(writerA, context);

		// A stale writer (a separate instance that has only seen version 1) attempts to
		// persist its own version 2, which has already been written.
		TestAggregate staleWriter = new() { Details = { Id = "agg-1" } };
		staleWriter.Increment(3);

		// Act & Assert
		await Assert
			.That(async () => await eventStore.SaveAsync(staleWriter, context))
			.Throws<Exceptions.ConcurrencyException>();
	}

	[Test]
	public async Task SaveAsync_GivenSequentialSaves_Succeeds()
	{
		// Arrange
		using var provider = CreateProvider();
		var eventStore = provider.GetRequiredService<IEventStore>();
		var context = CreateContext();

		var aggregate = await eventStore.CreateAsync<TestAggregate>("agg-2");
		aggregate.Increment(1);
		await eventStore.SaveAsync(aggregate, context);

		aggregate.Increment(2);
		var result = await eventStore.SaveAsync(aggregate, context);

		// Assert
		await Assert.That(result.Saved).IsTrue();
		await Assert.That(aggregate.Details.SavedVersion).IsEqualTo(2);
	}
}
