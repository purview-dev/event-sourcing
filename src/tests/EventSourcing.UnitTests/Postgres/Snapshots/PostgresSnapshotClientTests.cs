using System.Reflection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Postgres.Client;

namespace Purview.EventSourcing.Postgres.Snapshots;

public sealed class PostgresSnapshotClientTests
{
	[Test]
	public async Task ValidateAggregatePayloadShape_GivenUriProperty_DoesNotThrow()
	{
		await Assert.That(() => ValidateAggregatePayloadShape(typeof(UriAggregate))).ThrowsNothing();
	}

	sealed class UriAggregate : IAggregate
	{
		public string AggregateType => nameof(UriAggregate);

		public AggregateDetails Details { get; init; } = new();

		public Uri BlobUri { get; init; } = new("/", UriKind.Relative);

		public IReadOnlyList<EventRecord> GetUnsavedEvents() => [];

		public bool HasUnsavedEvents() => false;

		public IEnumerable<Type> GetRegisteredEventTypes() => [];

		public bool CanApplyEvent(object aggregateEvent) => false;

		public void ClearUnsavedEvents(int? upToVersion = null) { }

		void IAggregate.ApplyEvent(object @event, EventMetadata metadata) { }
	}

	static void ValidateAggregatePayloadShape(Type aggregateType)
	{
		var method =
			typeof(PostgresClient).GetMethod(
				"ValidateAggregatePayloadShape",
				BindingFlags.Static | BindingFlags.NonPublic
			) ?? throw new InvalidOperationException("Unable to locate ValidateAggregatePayloadShape via reflection.");

		try
		{
			method.Invoke(null, [aggregateType]);
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			throw ex.InnerException;
		}
	}
}
