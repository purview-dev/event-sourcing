using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.ValueObjects;

namespace Purview.EventSourcing.SqlServer.Snapshots;

[ClassDataSource<SqlServerSnapshotEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class SqlServerSnapshotValueObjectPersistenceTests(SqlServerSnapshotEventStoreFixture fixture)
{
	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Provider integration scenario intentionally exercises the complete value-object graph."
	)]
	public async Task SaveAsync_GivenComplexValueObjects_PersistsEventSnapshotAndSupportsSnapshotLinqQueries(
		CancellationToken cancellationToken
	)
	{
		var store = fixture.CreateSnapshotStore<SnapshotValueObjectsAggregate>();
		var aggregateId = Guid.NewGuid().ToString("D");
		var eventTableName = $"EventStore_{Guid.NewGuid():N}";
		var displayName = "Jane Snapshot";
		var displayName2 = "Jane Snapshot 2";

		SnapshotValueObjectsAggregate aggregate = new();
		aggregate.Details.Id = aggregateId;
		aggregate.CaptureUserDetails(
			UserDetails.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"), displayName, true),
			UserDetails2.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"), displayName2)
		);

		var saveResult = await store.SaveAsync(aggregate, operationContext: null, cancellationToken);

		var snapshotQuery = await store.QueryAsync(
			m => m.UserDetails.DisplayName == displayName && m.UserDetails2.DisplayName == displayName2,
			cancellationToken: cancellationToken
		);
		var snapshot = snapshotQuery.Results.SingleOrDefault();

		List<(EventRecord EventRecord, string EventType)> events = [];
		await foreach (
			var @event in store.GetEventRangeAsync(aggregateId, versionFrom: 1, versionTo: null, cancellationToken)
		)
		{
			events.Add(@event);
		}

		var eventPayload = events.Single().EventRecord.Event;
		var eventUserDetails = eventPayload
			.GetType()
			.GetProperty(nameof(SnapshotValueObjectsAggregate.UserDetails))!
			.GetValue(eventPayload)!;
		var eventUserDetails2 = eventPayload
			.GetType()
			.GetProperty(nameof(SnapshotValueObjectsAggregate.UserDetails2))!
			.GetValue(eventPayload)!;
		var eventDisplayName = (string)
			eventUserDetails.GetType().GetProperty(nameof(UserDetails.DisplayName))!.GetValue(eventUserDetails)!;
		var eventDisplayName2 = (string)
			eventUserDetails2.GetType().GetProperty(nameof(UserDetails2.DisplayName))!.GetValue(eventUserDetails2)!;

		await Assert.That(saveResult.Saved).IsTrue();
		await Assert.That(events).Count().IsEqualTo(1);
		await Assert.That(eventDisplayName).IsEqualTo(displayName);
		await Assert.That(eventDisplayName2).IsEqualTo(displayName2);
		await Assert.That(snapshot).IsNotNull();
		await Assert.That(snapshot!.UserDetails.DisplayName).IsEqualTo(displayName);
		await Assert.That(snapshot.UserDetails2.DisplayName).IsEqualTo(displayName2);
	}
}
