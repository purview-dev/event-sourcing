using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.SqlServer.Snapshots;

partial class SQLServerSnapshotEventStoreTests
{
	[Test]
	public async Task GetByIdAsync_GivenUnsupportedPayloadShape_ThrowsEarly()
	{
		Client.SqlServerClient client = new(
			new Client.SqlServerClientOptions(fixture.ConnectionString, false)
			{
				SchemaName = "dbo",
				TableName = $"Snapshots_{Guid.NewGuid():N}",
				AutoCreateTable = false,
			}
		);

		Task<UnsupportedPayloadAggregate?> Act() => client.GetByIdAsync<UnsupportedPayloadAggregate>("id");

		var ex = await Assert.That(Act).Throws<InvalidOperationException>();
		await Assert.That(ex).IsNotNull();
		await Assert.That(ex.Message).Contains(nameof(UnsupportedPayloadAggregate.UnsupportedMap));
	}

	sealed class UnsupportedPayloadAggregate : IAggregate
	{
		public string AggregateType => nameof(UnsupportedPayloadAggregate);

		public AggregateDetails Details { get; init; } = new();

		public IReadOnlyList<int> UnsupportedMap { get; init; } = [];

		public IReadOnlyList<EventRecord> GetUnsavedEvents() => [];

		public bool HasUnsavedEvents() => false;

		public IEnumerable<Type> GetRegisteredEventTypes() => [];

		public bool CanApplyEvent(object aggregateEvent) => false;

		public void ClearUnsavedEvents(int? upToVersion = null) { }

		void IAggregate.ApplyEvent(object @event, EventMetadata metadata) { }
	}
}
