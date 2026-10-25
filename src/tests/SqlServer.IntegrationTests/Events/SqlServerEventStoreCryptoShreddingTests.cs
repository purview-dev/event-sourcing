using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Fixtures.SqlServer;

namespace Purview.EventSourcing.SqlServer.Events;

/// <summary>
/// Guards crypto-shredding end to end against real SQL Server.
/// </summary>
/// <remarks>
/// Exercised against the database rather than in memory because the point is what reaches storage: the
/// stored payload must be ciphertext, and erasing the key must make it unreadable on the way back out.
/// </remarks>
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerAssembly)]
public sealed class SqlServerEventStoreCryptoShreddingTests(SqlServerEventStoreFixture fixture)
{
	static (InMemoryEventCryptoKeyStore Keys, AesGcmEventPayloadProtector Protector) CreateProtection()
	{
		InMemoryEventCryptoKeyStore keys = new();

		return (keys, new AesGcmEventPayloadProtector(keys));
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Reads raw storage to prove the payload is ciphertext, which needs both the store and the client."
	)]
	public async Task SaveAsync_GivenAnEventWithADataSubject_StoresCipherText(CancellationToken cancellationToken)
	{
		// Arrange
		var (_, protector) = CreateProtection();
		var runId = Guid.NewGuid();
		var aggregateId = $"{Guid.NewGuid()}";

		var store = fixture.CreateEventStore<PersistenceAggregate>(runId: runId, payloadProtector: protector);

		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		aggregate.SetSubjectName("Ada Lovelace", "user-4712");

		// Act
		var saved = await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Assert - the name must not be readable in the stored payload.
		await Assert.That(saved.IsSuccess).IsTrue();

		var payloads = await ReadRawEventPayloadsAsync(runId, aggregateId, cancellationToken);

		await Assert.That(payloads).IsNotEmpty();
		await Assert.That(payloads.Any(p => p.Contains("Ada Lovelace", StringComparison.Ordinal))).IsFalse();
		await Assert
			.That(payloads.Any(p => p.Contains(ProtectedPayload.MarkerPropertyName, StringComparison.Ordinal)))
			.IsTrue();
	}

	[Test]
	public async Task GetAsync_AfterShredding_SkipsTheEventAndStillReturnsTheAggregate(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var (keys, protector) = CreateProtection();
		var runId = Guid.NewGuid();
		var aggregateId = $"{Guid.NewGuid()}";

		var store = fixture.CreateEventStore<PersistenceAggregate>(runId: runId, payloadProtector: protector);

		var aggregate = TestHelpers.Aggregate<PersistenceAggregate>(aggregateId: aggregateId);
		aggregate.SetSubjectName("Ada Lovelace", "user-4712");
		await store.SaveAsync(aggregate, cancellationToken: cancellationToken);

		// Act - erase the subject, then replay.
		await keys.ShredAsync("user-4712", cancellationToken);

		var result = await store.GetAsync(aggregateId, cancellationToken: cancellationToken);

		// Assert - replay succeeds, the event is skipped, and the gap is visible to the caller.
		await Assert.That(result.IsSuccess).IsTrue();

		var skipped = (result.Value as AggregateBase)?.SkippedEvents;

		await Assert.That(skipped).IsNotNull();
		await Assert.That(skipped!.Count).IsGreaterThan(0);
	}

	async Task<List<string>> ReadRawEventPayloadsAsync(
		Guid runId,
		string aggregateId,
		CancellationToken cancellationToken
	)
	{
		List<string> payloads = [];

		await using Microsoft.Data.SqlClient.SqlConnection connection = new(fixture.ConnectionString);
		await connection.OpenAsync(cancellationToken);

		// The table name is a test-generated GUID, not user input; the aggregate id is parameterised.
#pragma warning disable CA2100
		await using var command = connection.CreateCommand();
		command.CommandText =
			$"SELECT Payload FROM [dbo].[EventStoreEvents_{runId:N}] WHERE AggregateId = @id AND EntityType = 1 AND Payload IS NOT NULL";
		command.Parameters.AddWithValue("@id", aggregateId);
#pragma warning restore CA2100

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
			payloads.Add(reader.GetString(0));

		return payloads;
	}
}
