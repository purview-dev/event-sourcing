namespace Purview.EventSourcing.CosmosDb.Snapshots;

// Cosmos DB pages through continuation tokens rather than offsets, so these cover the token's own
// semantics: it resumes where the previous page ended, and is null once the last page has been read.
partial class CosmosDbSnapshotEventStoreTests
{
	[Test]
	[Arguments(10, 3)]
	[Arguments(10, 10)]
	[Arguments(5, 20)]
	public async Task ListAsync_GivenPageSize_PagesThroughEveryAggregateExactlyOnce(
		int numberOfAggregates,
		int pageSize,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		await using var context = fixture.CreateContext(correlationIdsToGenerate: numberOfAggregates);

		HashSet<string> expectedIds = [];
		for (var index = 0; index < numberOfAggregates; index++)
		{
			var aggregate = CreateAggregate($"{index}_{context.RunId}");
			aggregate.IncrementInt32Value();

			var saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
			expectedIds.Add(aggregate.Details.Id);
		}

		// Act
		List<string> ids = [];
		var response = await context.EventStore.ListAsync(
			new ContinuationRequest { MaxRecords = pageSize },
			cancellationToken: cancellationToken
		);
		ids.AddRange(response.Results.Select(aggregate => aggregate.Details.Id));

		await Assert.That(response.Results.Length).IsEqualTo(Math.Min(pageSize, numberOfAggregates));

		while (response.ContinuationToken != null)
		{
			response = await context.EventStore.ListAsync(response.ToRequest(), cancellationToken: cancellationToken);
			ids.AddRange(response.Results.Select(aggregate => aggregate.Details.Id));
		}

		// Assert
		await Assert.That(ids.Count).IsEqualTo(numberOfAggregates);
		await Assert.That(ids.Distinct().Count()).IsEqualTo(numberOfAggregates);
		await Assert.That(ids.ToHashSet().SetEquals(expectedIds)).IsTrue();
	}

	[Test]
	public async Task ListAsync_GivenPageSizeLargerThanResultSet_ReturnsNullContinuationToken(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const int numberOfAggregates = 3;
		await using var context = fixture.CreateContext(correlationIdsToGenerate: numberOfAggregates);

		for (var index = 0; index < numberOfAggregates; index++)
		{
			var aggregate = CreateAggregate($"{index}_{context.RunId}");
			aggregate.IncrementInt32Value();

			var saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var response = await context.EventStore.ListAsync(
			new ContinuationRequest { MaxRecords = numberOfAggregates + 10 },
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(response.Results.Length).IsEqualTo(numberOfAggregates);
		await Assert.That(response.ContinuationToken).IsNull();
	}

	[Test]
	public async Task QueryAsync_GivenNoMatchingAggregates_ReturnsEmptyPageWithoutToken(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		await using var context = fixture.CreateContext();

		var aggregate = CreateAggregate($"no-match_{context.RunId}");
		aggregate.IncrementInt32Value();

		var saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saveResult.IsSuccess).IsTrue();

		// Act
		var response = await context.EventStore.QueryAsync(
			m => m.IncrementInt32 == -1,
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(response.Results).IsEmpty();
		await Assert.That(response.ContinuationToken).IsNull();
	}

	[Test]
	public async Task QueryAsync_GivenIncludeTotalCount_ReturnsTotalBeyondThePage(CancellationToken cancellationToken)
	{
		// Arrange
		const int numberOfAggregates = 6;
		const int pageSize = 2;

		await using var context = fixture.CreateContext(correlationIdsToGenerate: numberOfAggregates);

		for (var index = 0; index < numberOfAggregates; index++)
		{
			var aggregate = CreateAggregate($"{index}_{context.RunId}");
			aggregate.SetInt32Value(42);

			var saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);

			await Assert.That(saveResult.IsSuccess).IsTrue();
		}

		// Act
		var response = await context.EventStore.QueryAsync(
			m => m.Int32Value == 42,
			new ContinuationRequest { MaxRecords = pageSize, IncludeTotalCount = true },
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(response.Results.Length).IsEqualTo(pageSize);
		await Assert.That(response.TotalCount).IsEqualTo(numberOfAggregates);
	}
}
