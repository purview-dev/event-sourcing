using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Primitives;
using Purview.EventSourcing.Aggregates.Persistence;

namespace Purview.EventSourcing.CosmosDb.Snapshots;

partial class CosmosDbSnapshotEventStoreTests
{
	[Test]
	[Arguments(1)]
	[Arguments(5)]
	[Arguments(10)]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments")]
	public async Task CanQuery_GivenAggregatesContainsDictionaryWithStringValuesAsValue_QueryAsExpected(
		int numberOfAggregates,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		await using var context = fixture.CreateContext(correlationIdsToGenerate: numberOfAggregates);

		var aggregateType = CreateAggregate().AggregateType;
		PartitionKey partitionKey = new(aggregateType);

		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"{aggregateIndex}_{context.RunId}");
			aggregate.AddKVPs([
				new KeyValuePair<string, StringValues>("name-1", "value-1"),
				new KeyValuePair<string, StringValues>("name-2", new[] { "value-100", "value-200" }),
			]);

			bool saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult).IsTrue();
		}

		// Act
		var aggregates = (
			await context.CosmosDbClient.QueryAsync<PersistenceAggregate>(
				m => m.StringValuesDictionary["name-1"] == "value-1",
				partitionKey,
				maxRecords: numberOfAggregates,
				cancellationToken: cancellationToken
			)
		).Results;

		// Assert
		await Assert.That(aggregates.Length).IsEqualTo(numberOfAggregates);
	}

	[Test]
	[Arguments(1)]
	[Arguments(5)]
	[Arguments(10)]
	public async Task CanQuery_GivenAggregatesContainsDictionaryWithStringsAsValues_QueryAsExpected(
		int numberOfAggregates,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		await using var context = fixture.CreateContext(correlationIdsToGenerate: numberOfAggregates);

		var aggregateType = CreateAggregate().AggregateType;
		PartitionKey partitionKey = new(aggregateType);

		for (var aggregateIndex = 0; aggregateIndex < numberOfAggregates; aggregateIndex++)
		{
			var aggregate = CreateAggregate($"{aggregateIndex}_{context.RunId}");
			aggregate.AddKVPs([
				new KeyValuePair<string, string>("name-1", "value-1"),
				new KeyValuePair<string, string>("name-2", "value-100"),
			]);

			bool saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult).IsTrue();
		}

		// Act
#pragma warning disable CA1304 // Specify CultureInfo
#pragma warning disable CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons
#pragma warning disable CA1311 // Specify a culture or use an invariant version
		var aggregates = (
			await context.CosmosDbClient.QueryAsync<PersistenceAggregate>(
				m => m.StringsDictionary["name-1"].ToLower() == "value-1",
				partitionKey,
				maxRecords: numberOfAggregates,
				cancellationToken: cancellationToken
			)
		).Results;
#pragma warning restore CA1311 // Specify a culture or use an invariant version
#pragma warning restore CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons
#pragma warning restore CA1304 // Specify CultureInfo

		// Assert
		await Assert.That(aggregates.Length).IsEqualTo(numberOfAggregates);
	}
}
