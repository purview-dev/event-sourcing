using System.Reflection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.EntityFrameworkCore;
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

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenOpaqueReadOnlyCollection_DoesNotThrow()
	{
		await Assert.That(() => ValidateAggregatePayloadShape(typeof(OpaqueMemberAggregate))).ThrowsNothing();
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenReadOnlyComplexMemberOnValueType_Throws()
	{
		var exception = await Assert
			.That(() => ValidateAggregatePayloadShape(typeof(ReadOnlyValueTypeMemberAggregate)))
			.Throws<InvalidOperationException>();

		await Assert.That(exception.Message).Contains(nameof(ReadOnlyStructHolder.Mirror));
		await Assert.That(exception.Message).Contains("writeable");
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenSettableComplexMemberOnValueType_DoesNotThrow()
	{
		await Assert
			.That(() => ValidateAggregatePayloadShape(typeof(SettableValueTypeMemberAggregate)))
			.ThrowsNothing();
	}

	sealed class OpaqueMemberAggregate
	{
		[EFOpaque]
		public IReadOnlyList<int> Values { get; init; } = [];
	}

	sealed class ReadOnlyValueTypeMemberAggregate
	{
		public ReadOnlyStructHolder Holder { get; init; }
	}

	sealed class SettableValueTypeMemberAggregate
	{
		public SettableStructHolder Holder { get; init; }
	}

	readonly struct ReadOnlyStructHolder
	{
		ReadOnlyStructHolder(ValueTypeMemberInfo mirror) => Mirror = mirror;

		public ValueTypeMemberInfo Mirror { get; }
	}

	readonly struct SettableStructHolder
	{
		public ValueTypeMemberInfo Mirror { get; init; }
	}

	sealed class ValueTypeMemberInfo
	{
		public string? Name { get; set; }
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
