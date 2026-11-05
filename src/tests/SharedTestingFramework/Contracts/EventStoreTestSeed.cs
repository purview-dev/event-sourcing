using System.Security.Cryptography;
using System.Text;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.Contracts;

/// <summary>
/// Provider-agnostic arrange/assert helpers used by the shared event-store contract suite.
/// This file is linked (not compiled) into each provider integration test project.
/// </summary>
public static class EventStoreTestSeed
{
	public static ComplexTestType CreateComplexTestType()
	{
		return new()
		{
			Int16Property = (short)RandomNumberGenerator.GetInt32(short.MinValue, short.MaxValue),
			Int32Property = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue),
			Int64Property = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue) * 5L,
			StringProperty = $"{Guid.NewGuid()}",
			DateTimeOffsetProperty = DateTimeOffset.UtcNow.AddYears(RandomNumberGenerator.GetInt32(100, 1001)),
			ComplexNestedTestTypeProperty = new() { Nested = $"Nested_{Guid.NewGuid()}" },
		};
	}

	public static string BuildLargeString()
	{
		var value = string.Empty;
		var sizeIsLessThan32K = true;
		while (sizeIsLessThan32K)
		{
			value += "abcdefghijklmnopqrstvwxyz";
			value += "ABCDEFGHIJKLMNOPQRSTVWXYZ";
			value += "1234567890";

			sizeIsLessThan32K = Encoding.UTF8.GetByteCount(value) < short.MaxValue;
		}

		return value;
	}

	public static TAggregate BuildAggregateWithIncrementEvents<TAggregate>(string aggregateId, int eventCount)
		where TAggregate : class, IAggregateTest, new()
	{
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);

		for (var i = 0; i < eventCount; i++)
			aggregate.IncrementInt32Value();

		return aggregate;
	}

	public static TAggregate BuildAggregateWithOldEvents<TAggregate>(
		string aggregateId,
		int eventsToCreate,
		int numberOfOldEventsToCreate
	)
		where TAggregate : class, IAggregateTest, new()
	{
		var aggregate = TestHelpers.Aggregate<TAggregate>(aggregateId: aggregateId);
		// Register the event type here...!
		aggregate.RegisterOldEventType();

		for (var i = 0; i < eventsToCreate; i++)
			aggregate.IncrementInt32Value();

		for (var i = 0; i < numberOfOldEventsToCreate; i++)
			aggregate.SetOldEventValue(Guid.NewGuid());

		return aggregate;
	}

	public static async Task AssertRecreatedMatchesSource<TAggregate>(TAggregate? result, TAggregate aggregate)
		where TAggregate : class, IAggregateTest, new()
	{
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Id()).IsEqualTo(aggregate.Id());
		await Assert.That(result.IncrementInt32).IsEqualTo(aggregate.IncrementInt32);
		await Assert.That(result.Details.SavedVersion).IsEqualTo(aggregate.Details.SavedVersion);
		await Assert.That(result.Details.CurrentVersion).IsEqualTo(aggregate.Details.CurrentVersion);
		await Assert.That(result.Details.SnapshotVersion).IsEqualTo(aggregate.Details.SnapshotVersion);
	}

	public static async Task AssertRecreatedWithTotals<TAggregate>(
		TAggregate? result,
		TAggregate aggregate,
		int totalEvents
	)
		where TAggregate : class, IAggregateTest, new()
	{
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.IsNew()).IsFalse();
		await Assert.That(result.Id()).IsEqualTo(aggregate.Id());
		await Assert.That(result.IncrementInt32).IsEqualTo(aggregate.IncrementInt32);
		await Assert.That(result.Details.SavedVersion).IsEqualTo(totalEvents);
		await Assert.That(result.Details.CurrentVersion).IsEqualTo(totalEvents);
	}

	public static void SetRandomComplexProperty<TAggregate>(TAggregate aggregate)
		where TAggregate : class, IAggregateTest, new() => aggregate.SetComplexProperty(CreateComplexTestType());

	public static async Task AssertComplexPropertyMatches<TAggregate>(TAggregate aggregate, TAggregate? result)
		where TAggregate : class, IAggregateTest, new()
	{
		await Assert.That(result).IsNotNull();
		await Assert.That(aggregate.ComplexTestType).IsEquivalentTo(result!.ComplexTestType);
	}

	public static async Task AssertSaveThrowsArgumentOutOfRangeException<TAggregate>(
		IEventStoreCore<TAggregate> eventStore,
		TAggregate aggregate,
		CancellationToken cancellationToken
	)
		where TAggregate : class, IAggregateTest, new()
	{
		// Act
		async Task<Result<SaveOutcome<TAggregate>, AggregateSaveFailure>> Func() =>
			await eventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(Func).Throws<ArgumentOutOfRangeException>();
	}

	public static async Task AssertGetAsyncReportsDeleted<TAggregate>(
		IEventStoreCore<TAggregate> eventStore,
		string aggregateId,
		CancellationToken cancellationToken
	)
		where TAggregate : class, IAggregateTest, new()
	{
		// Act
		var result = await eventStore.GetAsync(aggregateId, null, cancellationToken: cancellationToken);

		// Assert
		await Assert.That(result.IsFailure).IsTrue();
		await Assert.That(result.Error is AggregateDeleted).IsTrue();
	}
}
