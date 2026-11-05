using System.Linq.Expressions;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.EntityFrameworkCore.Mapping;
using Purview.EventSourcing.EntityFrameworkCore.Query;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.EntityFrameworkCore.Snapshots;

// The snapshot shape rules and predicate rewriting are shared by every EF-backed provider
// (SQL Server, Postgres and Cosmos DB), so they are covered once, here.
public sealed class SnapshotModelMapperTests
{
	[Test]
	public async Task ValidateAggregatePayloadShape_GivenEventStoreCollections_DoesNotThrow()
	{
		await Assert
			.That(() => SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(SupportedCollectionAggregate)))
			.ThrowsNothing();
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenArrayCollection_Throws()
	{
		var ex = await Assert
			.That(() => SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(ArrayCollectionAggregate)))
			.Throws<InvalidOperationException>();
		await Assert.That(ex).IsNotNull();
		await Assert.That(ex!.Message).Contains(nameof(ArrayCollectionAggregate.Values));
		await Assert.That(ex.Message).Contains("EventStoreList<T>");
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenIReadOnlyListCollection_Throws()
	{
		var ex = await Assert
			.That(() => SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(ReadOnlyListCollectionAggregate)))
			.Throws<InvalidOperationException>();
		await Assert.That(ex).IsNotNull();
		await Assert.That(ex!.Message).Contains(nameof(ReadOnlyListCollectionAggregate.Values));
		await Assert.That(ex.Message).Contains("EventStoreSet<T>");
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenUriProperty_DoesNotThrow()
	{
		await Assert
			.That(() => SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(UriAggregate)))
			.ThrowsNothing();
	}

	[Test]
	public async Task Rewrite_GivenScalarEqualsPrimitive_RewritesToPrimitiveComparison()
	{
		// Arrange
		Expression<Func<ScalarHolder, bool>> whereClause = model => model.Email == "updated@test.com";

		// Act
		var rewritten = SnapshotPredicateRewriter.Rewrite(whereClause, nameof(ScalarHolder));

		// Assert
		var binaryExpression = rewritten.Body as BinaryExpression;
		await Assert.That(binaryExpression).IsNotNull();
		await Assert.That(binaryExpression!.NodeType).IsEqualTo(ExpressionType.Equal);
		await Assert.That(binaryExpression.Left.Type).IsEqualTo(typeof(string));
		await Assert.That(binaryExpression.Right.Type).IsEqualTo(typeof(string));
	}

	[Test]
	public async Task Rewrite_GivenAggregateTypeComparison_FoldsToConstant()
	{
		// Arrange
		Expression<Func<UriAggregate, bool>> whereClause = model =>
			model.AggregateType == nameof(UriAggregate) && model.BlobUri != null;

		// Act
		var rewritten = SnapshotPredicateRewriter.Rewrite(whereClause, nameof(UriAggregate));

		// Assert
		var binaryExpression = (BinaryExpression)rewritten.Body;
		var aggregateTypeComparison = (BinaryExpression)binaryExpression.Left;
		await Assert.That(aggregateTypeComparison.Left).IsTypeOf<ConstantExpression>();
	}

	[Test]
	public async Task Rewrite_GivenInvariantCasing_NormalizesToTranslatableOverload()
	{
#pragma warning disable CA1308 // Normalize strings to uppercase
#pragma warning disable CA1862 // Use the 'StringComparison' method overloads
		// Arrange
		Expression<Func<UriAggregate, bool>> whereClause = model => model.Name.ToLowerInvariant() == "value";

		// Act
		var rewritten = SnapshotPredicateRewriter.Rewrite(whereClause, nameof(UriAggregate));
#pragma warning restore CA1862 // Use the 'StringComparison' method overloads
#pragma warning restore CA1308 // Normalize strings to uppercase

		// Assert
		var binaryExpression = (BinaryExpression)rewritten.Body;
		var methodCall = (MethodCallExpression)binaryExpression.Left;
		await Assert.That(methodCall.Method.Name).IsEqualTo(nameof(string.ToLower));
	}

	[Test]
	public async Task EnsureSupportedOrderByExpression_GivenScalarInnerMemberOrdering_Throws()
	{
		// Arrange
		var query = Array.Empty<ScalarHolder>().AsQueryable().OrderBy(model => model.Email.Value);

		// Act & Assert
		var exception = await Assert
			.That(() => SnapshotPredicateRewriter.EnsureSupportedOrderByExpression(query.Expression))
			.Throws<InvalidOperationException>();

		await Assert.That(exception!.Message).Contains(nameof(ScalarEmail.Value));
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenOpaqueReadOnlyCollection_DoesNotThrow()
	{
		await Assert
			.That(() => SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(OpaqueMemberAggregate)))
			.ThrowsNothing();
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenReadOnlyComplexMemberOnValueType_Throws()
	{
		var exception = await Assert
			.That(() => SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(ReadOnlyValueTypeMemberAggregate)))
			.Throws<InvalidOperationException>();

		await Assert.That(exception!.Message).Contains(nameof(ReadOnlyStructHolder.Mirror));
		await Assert.That(exception.Message).Contains("writeable");
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenReadOnlyComplexCollectionMemberOnValueType_Throws()
	{
		var exception = await Assert
			.That(() =>
				SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(ReadOnlyValueTypeCollectionMemberAggregate))
			)
			.Throws<InvalidOperationException>();

		await Assert.That(exception!.Message).Contains(nameof(ReadOnlyCollectionHolder.Items));
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenSettableComplexMemberOnValueType_DoesNotThrow()
	{
		await Assert
			.That(() => SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(SettableValueTypeMemberAggregate)))
			.ThrowsNothing();
	}

	sealed class ScalarHolder
	{
		public ScalarEmail Email { get; init; } = new("default@test.com");
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

	sealed class ReadOnlyValueTypeCollectionMemberAggregate
	{
		public ReadOnlyCollectionHolder Holder { get; init; }
	}

	sealed class SettableValueTypeMemberAggregate
	{
		public SettableStructHolder Holder { get; init; }
	}

	readonly struct ReadOnlyStructHolder
	{
#pragma warning disable IDE0051 // Remove unused private members
		ReadOnlyStructHolder(ValueTypeMemberInfo mirror) => Mirror = mirror;
#pragma warning restore IDE0051 // Remove unused private members

		public ValueTypeMemberInfo Mirror { get; }
	}

	readonly struct ReadOnlyCollectionHolder
	{
#pragma warning disable IDE0051 // Remove unused private members
		ReadOnlyCollectionHolder(EventStoreList<ValueTypeMemberInfo> items) => Items = items;
#pragma warning restore IDE0051 // Remove unused private members

		public EventStoreList<ValueTypeMemberInfo> Items { get; }
	}

	readonly struct SettableStructHolder
	{
		public ValueTypeMemberInfo Mirror { get; init; }
	}

	sealed class ValueTypeMemberInfo
	{
		public string? Name { get; set; }
	}

	sealed class UriAggregate : IAggregate
	{
		public string AggregateType => nameof(UriAggregate);

		public AggregateDetails Details { get; init; } = new();

		public Uri BlobUri { get; init; } = new("/", UriKind.Relative);

		public string Name { get; init; } = string.Empty;

		public IReadOnlyList<EventRecord> GetUnsavedEvents() => [];

		public bool HasUnsavedEvents() => false;

		public IEnumerable<Type> GetRegisteredEventTypes() => [];

		public bool CanApplyEvent(object aggregateEvent) => false;

		public void ClearUnsavedEvents(int? upToVersion = null) { }

		void IAggregate.ApplyEvent(object @event, EventMetadata metadata) { }
	}

	sealed class SupportedCollectionAggregate : IAggregate
	{
		public string AggregateType => nameof(SupportedCollectionAggregate);

		public AggregateDetails Details { get; init; } = new();

		public EventStoreList<int> IntValues { get; init; } = new();

		public EventStoreSet<string> StringValues { get; init; } = new();

		public IReadOnlyList<EventRecord> GetUnsavedEvents() => [];

		public bool HasUnsavedEvents() => false;

		public IEnumerable<Type> GetRegisteredEventTypes() => [];

		public bool CanApplyEvent(object aggregateEvent) => false;

		public void ClearUnsavedEvents(int? upToVersion = null) { }

		void IAggregate.ApplyEvent(object @event, EventMetadata metadata) { }
	}

	sealed class ArrayCollectionAggregate : IAggregate
	{
		public string AggregateType => nameof(ArrayCollectionAggregate);

		public AggregateDetails Details { get; init; } = new();

		public int[] Values { get; init; } = [];

		public IReadOnlyList<EventRecord> GetUnsavedEvents() => [];

		public bool HasUnsavedEvents() => false;

		public IEnumerable<Type> GetRegisteredEventTypes() => [];

		public bool CanApplyEvent(object aggregateEvent) => false;

		public void ClearUnsavedEvents(int? upToVersion = null) { }

		void IAggregate.ApplyEvent(object @event, EventMetadata metadata) { }
	}

	sealed class ReadOnlyListCollectionAggregate : IAggregate
	{
		public string AggregateType => nameof(ReadOnlyListCollectionAggregate);

		public AggregateDetails Details { get; init; } = new();

		public IReadOnlyList<int> Values { get; init; } = [];

		public IReadOnlyList<EventRecord> GetUnsavedEvents() => [];

		public bool HasUnsavedEvents() => false;

		public IEnumerable<Type> GetRegisteredEventTypes() => [];

		public bool CanApplyEvent(object aggregateEvent) => false;

		public void ClearUnsavedEvents(int? upToVersion = null) { }

		void IAggregate.ApplyEvent(object @event, EventMetadata metadata) { }
	}
}

[Scalar(
	GenerateJsonConverter = false,
	GenerateComparable = false,
	GenerateComparisonOperators = false,
	GenerateEnumProperties = false,
	GenerateImplicitFromPrimitive = false,
	GenerateImplicitToPrimitive = false,
	GenerateEmpty = false
)]
readonly partial record struct ScalarEmail
{
	public string Value { get; }

	public ScalarEmail(string value) => Value = value;

	public static bool operator ==(ScalarEmail left, string right) => left.Value == right;

	public static bool operator !=(ScalarEmail left, string right) => !(left == right);

	public static implicit operator string(ScalarEmail value) => value.Value;
}
