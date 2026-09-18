using System.Linq.Expressions;
using System.Reflection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.SqlServer.Client;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.SqlServer.Snapshots;

public sealed class SqlServerSnapshotClientTests
{
	[Test]
	public async Task Constructor_GivenDefaultOptions_DoesNotUseLegacyEnsureTableSql()
	{
		// Arrange & Act
		SqlServerClient client = new(
			new SqlServerClientOptions("Server=.;Database=Test;Trusted_Connection=True;", false)
			{
				SchemaName = "dbo",
				TableName = "Snapshots",
				AutoCreateTable = false,
			}
		);

		var ensureTableSqlField = typeof(SqlServerClient).GetField(
			"_ensureTableSql",
			BindingFlags.Instance | BindingFlags.NonPublic
		);

		// Assert
		await Assert.That(client).IsNotNull();
		await Assert.That(ensureTableSqlField).IsNull();
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenEventStoreCollections_DoesNotThrow()
	{
		await Assert.That(() => ValidateAggregatePayloadShape(typeof(SupportedCollectionAggregate))).ThrowsNothing();
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenArrayCollection_Throws()
	{
		var ex = await Assert
			.That(() => ValidateAggregatePayloadShape(typeof(ArrayCollectionAggregate)))
			.Throws<InvalidOperationException>();
		await Assert.That(ex).IsNotNull();
		await Assert.That(ex.Message).Contains(nameof(ArrayCollectionAggregate.Values));
		await Assert.That(ex.Message).Contains("EventStoreList<T>");
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenIReadOnlyListCollection_Throws()
	{
		var ex = await Assert
			.That(() => ValidateAggregatePayloadShape(typeof(ReadOnlyListCollectionAggregate)))
			.Throws<InvalidOperationException>();
		await Assert.That(ex).IsNotNull();
		await Assert.That(ex.Message).Contains(nameof(ReadOnlyListCollectionAggregate.Values));
		await Assert.That(ex.Message).Contains("EventStoreSet<T>");
	}

	[Test]
	public async Task ValidateAggregatePayloadShape_GivenUriProperty_DoesNotThrow()
	{
		await Assert.That(() => ValidateAggregatePayloadShape(typeof(UriAggregate))).ThrowsNothing();
	}

	[Test]
	public async Task RewriteAggregateTypePredicate_GivenScalarEqualsPrimitive_RewritesToPrimitiveComparison()
	{
		Expression<Func<ScalarHolder, bool>> whereClause = model => model.Email == "updated@test.com";
		var method = typeof(SqlServerClient).GetMethod(
			"RewriteAggregateTypePredicate",
			BindingFlags.Static | BindingFlags.NonPublic
		);
		await Assert.That(method).IsNotNull();

		var genericMethod = method!.MakeGenericMethod(typeof(ScalarHolder));
		var rewritten =
			(Expression<Func<ScalarHolder, bool>>)genericMethod.Invoke(null, [whereClause, nameof(ScalarHolder)])!;

		var binaryExpression = rewritten.Body as BinaryExpression;
		await Assert.That(binaryExpression).IsNotNull();
		await Assert.That(binaryExpression!.NodeType).IsEqualTo(ExpressionType.Equal);
		await Assert.That(binaryExpression.Left.Type).IsEqualTo(typeof(string));
		await Assert.That(binaryExpression.Right.Type).IsEqualTo(typeof(string));
	}

	sealed class ScalarHolder
	{
		public ScalarEmail Email { get; init; } = new("default@test.com");
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

	static void ValidateAggregatePayloadShape(Type aggregateType)
	{
		var method =
			typeof(SqlServerClient).GetMethod(
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
