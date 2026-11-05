using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.CosmosDb.Snapshots;

// Predicates are translated by Entity Framework against the document root, so these cover the shapes
// that depend on that mapping: string functions, culture-invariant casing (which is rewritten to the
// translatable overload), and members of a nested complex type.
partial class CosmosDbSnapshotEventStoreTests
{
	[Test]
	public async Task QueryAsync_GivenStartsWithPredicate_TranslatesAndMatches(CancellationToken cancellationToken)
	{
		// Arrange
		await using var context = fixture.CreateContext();

		var prefix = $"prefix-{context.RunId}";
		var aggregate = CreateAggregate($"starts-with_{context.RunId}");
		aggregate.AppendString($"{prefix}-value");

		var saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saveResult.IsSuccess).IsTrue();

		// Act
		var result = await context.EventStore.SingleOrDefaultAsync(
			m => m.StringProperty.StartsWith(prefix, StringComparison.Ordinal),
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Details.Id).IsEqualTo(aggregate.Details.Id);
	}

	[Test]
	public async Task QueryAsync_GivenContainsPredicate_TranslatesAndMatches(CancellationToken cancellationToken)
	{
		// Arrange
		await using var context = fixture.CreateContext();

		var fragment = $"fragment-{context.RunId}";
		var aggregate = CreateAggregate($"contains_{context.RunId}");
		aggregate.AppendString($"leading-{fragment}-trailing");

		var saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saveResult.IsSuccess).IsTrue();

		// Act
		var result = await context.EventStore.SingleOrDefaultAsync(
			m => m.StringProperty.Contains(fragment, StringComparison.Ordinal),
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Details.Id).IsEqualTo(aggregate.Details.Id);
	}

	[Test]
	public async Task QueryAsync_GivenInvariantLowerCasePredicate_TranslatesAndMatches(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		await using var context = fixture.CreateContext();

		var value = $"MIXED-Case-{context.RunId}";
		var aggregate = CreateAggregate($"casing_{context.RunId}");
		aggregate.AppendString(value);

		var saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saveResult.IsSuccess).IsTrue();

#pragma warning disable CA1308 // Normalize strings to uppercase
#pragma warning disable CA1862 // Use the 'StringComparison' method overloads
		var expected = value.ToLowerInvariant();

		// Act
		// ToLowerInvariant has no Cosmos DB translation; the shared predicate rewriter normalizes it to
		// ToLower, which maps onto LOWER.
		var result = await context.EventStore.SingleOrDefaultAsync(
			m => m.StringProperty.ToLowerInvariant() == expected,
			cancellationToken: cancellationToken
		);
#pragma warning restore CA1862 // Use the 'StringComparison' method overloads
#pragma warning restore CA1308 // Normalize strings to uppercase

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Details.Id).IsEqualTo(aggregate.Details.Id);
	}

	[Test]
	public async Task QueryAsync_GivenComplexMemberPredicate_TranslatesAndMatches(CancellationToken cancellationToken)
	{
		// Arrange
		await using var context = fixture.CreateContext();

		var complexValue = $"complex-{context.RunId}";
		var aggregate = CreateAggregate($"complex_{context.RunId}");
		aggregate.SetComplexProperty(
			new ComplexTestType
			{
				Int16Property = 16,
				Int32Property = 32,
				Int64Property = 64,
				StringProperty = complexValue,
				DateTimeOffsetProperty = DateTimeOffset.UtcNow,
			}
		);

		var saveResult = await context.EventStore.SaveAsync(aggregate, cancellationToken: cancellationToken);
		await Assert.That(saveResult.IsSuccess).IsTrue();

		// Act
		var result = await context.EventStore.SingleOrDefaultAsync(
			m => m.ComplexTestType!.StringProperty == complexValue,
			cancellationToken: cancellationToken
		);

		// Assert
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Details.Id).IsEqualTo(aggregate.Details.Id);
		await Assert.That(result.ComplexTestType!.Int32Property).IsEqualTo(32);
	}
}
