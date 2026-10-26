using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.Aggregates.Test;
using Purview.EventSourcing.Internal;
using Purview.EventSourcing.SqlServer.Snapshot;

namespace Purview.EventSourcing.SqlServer.Snapshots;

public sealed class SqlServerSnapshotEventStoreTests
{
	[Test]
	public async Task Constructor_GivenValidParameters_CreatesInstance()
	{
		// Arrange & Act
		var store = CreateStore();

		// Assert
		await Assert.That(store).IsNotNull();
	}

	[Test]
	public async Task CreateAsync_GivenAggregateId_DelegatesToEventStore()
	{
		// Arrange
		var expectedAggregate = TestHelpers.Aggregate<TestAggregate>();
		var eventStore = INonQueryableEventStore<TestAggregate>.Mock();
		eventStore.CreateAsync(Any<string?>(), Any<CancellationToken>()).Returns(expectedAggregate);

		var store = CreateStore(eventStore);

		// Act
		var result = await store.CreateAsync("test-id");

		// Assert
		await Assert.That(result).IsEqualTo(expectedAggregate);
		eventStore.CreateAsync("test-id", Any<CancellationToken>()).WasCalled(Times.Once);
	}

	[Test]
	public async Task GetAsync_GivenAggregateId_DelegatesToEventStore()
	{
		// Arrange
		var expectedAggregate = TestHelpers.Aggregate<TestAggregate>();
		var eventStore = INonQueryableEventStore<TestAggregate>.Mock();
		eventStore
			.GetAsync(Any<string>(), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.Returns(expectedAggregate);

		var store = CreateStore(eventStore);

		// Act
		var resultResult = await store.GetAsync("test-id", null);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

		// Assert
		await Assert.That(result).IsEqualTo(expectedAggregate);
		eventStore
			.GetAsync(Is("test-id"), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.WasCalled(Times.Once);
	}

	[Test]
	public async Task GetOrCreateAsync_GivenAggregateId_DelegatesToEventStore(CancellationToken cancellationToken)
	{
		// Arrange
		var expectedAggregate = TestHelpers.Aggregate<TestAggregate>();
		var eventStore = INonQueryableEventStore<TestAggregate>.Mock();
		eventStore
			.GetOrCreateAsync(Any<string?>(), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(expectedAggregate);

		var store = CreateStore(eventStore);

		// Act
		var resultResult = await store.GetOrCreateAsync("test-id", null, cancellationToken);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

		// Assert
		await Assert.That(result).IsEqualTo(expectedAggregate);
		eventStore
			.GetOrCreateAsync("test-id", Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.WasCalled(Times.Once);
	}

	[Test]
	public async Task GetAtAsync_GivenAggregateIdAndVersion_DelegatesToEventStore(CancellationToken cancellationToken)
	{
		// Arrange
		var expectedAggregate = TestHelpers.Aggregate<TestAggregate>();
		var eventStore = INonQueryableEventStore<TestAggregate>.Mock();
		eventStore
			.GetAtAsync(Any<string>(), Any<long>(), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.Returns(expectedAggregate);

		var store = CreateStore(eventStore);

		// Act
		var resultResult = await store.GetAtAsync("test-id", 5, null, cancellationToken);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

		// Assert
		await Assert.That(result).IsEqualTo(expectedAggregate);
		eventStore
			.GetAtAsync(Is("test-id"), Is(5L), Any<EventStoreOperationContext?>(), Is(cancellationToken))
			.WasCalled(Times.Once);
	}

	[Test]
	public async Task GetDeletedAsync_DelegatesToEventStore(CancellationToken cancellationToken)
	{
		// Arrange
		var expectedAggregate = TestHelpers.Aggregate<TestAggregate>();
		var eventStore = INonQueryableEventStore<TestAggregate>.Mock();
		eventStore.GetDeletedAsync(Any<string>(), Any<CancellationToken>()).Returns(expectedAggregate);

		var store = CreateStore(eventStore);

		// Act
		var resultResult = await store.GetDeletedAsync("test-id", cancellationToken);
		var result = resultResult.IsSuccess ? resultResult.Value : null;

		// Assert
		await Assert.That(result).IsEqualTo(expectedAggregate);
		eventStore.GetDeletedAsync(Is("test-id"), Is(cancellationToken)).WasCalled(Times.Once);
	}

	[Test]
	public async Task ExistsAsync_DelegatesToEventStore(CancellationToken cancellationToken)
	{
		// Arrange
		var expectedState = AggregateExistence.Exists;
		var eventStore = INonQueryableEventStore<TestAggregate>.Mock();
		eventStore.ExistsAsync(Any<string>(), Any<CancellationToken>()).Returns(expectedState);

		var store = CreateStore(eventStore);

		// Act
		var result = await store.ExistsAsync("test-id", cancellationToken);

		// Assert
		await Assert.That(result).IsEqualTo(expectedState);
		eventStore.ExistsAsync(Is("test-id"), Any<CancellationToken>()).WasCalled(Times.Once);
	}

	[Test]
	public async Task FulfilRequirements_DelegatesToEventStore()
	{
		// Arrange
		var expectedAggregate = TestHelpers.Aggregate<TestAggregate>();
		var eventStore = INonQueryableEventStore<TestAggregate>.Mock();
		eventStore.FulfilRequirements(Any<TestAggregate>()).Returns(expectedAggregate);

		var store = CreateStore(eventStore);

		// Act
		var result = store.FulfilRequirements(expectedAggregate);

		// Assert
		await Assert.That(result).IsEqualTo(expectedAggregate);
		eventStore.FulfilRequirements(expectedAggregate).WasCalled(Times.Once);
	}

	[Test]
	public async Task SaveAsync_GivenNeverSnapshotStrategy_DoesNotWriteSnapshot(CancellationToken cancellationToken)
	{
		var aggregate = TestHelpers.Aggregate<TestAggregate>(creator: a => a.RecordEvent(), clearEvents: false);
		var eventStore = INonQueryableEventStore<TestAggregate>.Mock();
		eventStore
			.SaveAsync(Any<TestAggregate>(), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.Returns(static (a, _, _) => new SaveOutcome<TestAggregate>(a, SaveOutcomeKind.Written));
		var store = CreateStore(eventStore, snapshotStrategy: new NeverSnapshotStrategy<TestAggregate>());

		var result = await store.SaveAsync(aggregate, null, cancellationToken);

		await Assert.That(result.IsSuccess && result.Value.Written).IsTrue();
		eventStore
			.SaveAsync(Is(aggregate), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.WasCalled(Times.Once);
	}

	[Test]
	public async Task SaveAsync_GivenContextSnapshotStrategy_OverridesDefaultStrategy(
		CancellationToken cancellationToken
	)
	{
		var aggregate = TestHelpers.Aggregate<TestAggregate>(creator: a => a.RecordEvent(), clearEvents: false);
		var eventStore = INonQueryableEventStore<TestAggregate>.Mock();
		eventStore
			.SaveAsync(Any<TestAggregate>(), Any<EventStoreOperationContext?>(), Any<CancellationToken>())
			.Returns(static (a, _, _) => new SaveOutcome<TestAggregate>(a, SaveOutcomeKind.Written));

		var store = CreateStore(eventStore, snapshotStrategy: new AlwaysSnapshotStrategy<TestAggregate>());
		var context = new EventStoreOperationContext().SetSnapshotStrategy(new NeverSnapshotStrategy<TestAggregate>());

		var result = await store.SaveAsync(aggregate, context, cancellationToken);

		await Assert.That(result.IsSuccess && result.Value.Written).IsTrue();
		eventStore.SaveAsync(Is(aggregate), context, Any<CancellationToken>()).WasCalled(Times.Once);
	}

	[Test]
	public async Task Constructor_GivenOptionsWithAggregateTableOverride_CreatesStoreWithoutThrowing()
	{
		// Arrange
		var options = CreateDefaultOptions();
		options.AggregateTableOverrides["Test"] = new SqlServerSnapshotAggregateTableOverride
		{
			SchemaName = "custom",
			TableName = "CustomSnapshots",
		};

		// Act & Assert — should not throw
		var store = CreateStore(options: options);
		await Assert.That(store).IsNotNull();
	}

	[Test]
	public async Task Constructor_GivenOptionsWithPartialAggregateTableOverride_FallsBackToGlobalDefaults()
	{
		// Arrange — only schema override, table should fall back to global default
		var options = CreateDefaultOptions();
		options.AggregateTableOverrides["Test"] = new SqlServerSnapshotAggregateTableOverride
		{
			SchemaName = "custom",
			// TableName not set → falls back to global "TestSnapshots"
		};

		// Act & Assert — should not throw
		var store = CreateStore(options: options);
		await Assert.That(store).IsNotNull();
	}

	[Test]
	public async Task Constructor_GivenDefaults_JsonIndexOptionsIsInitialized()
	{
		var options = CreateDefaultOptions();

		await Assert.That(options.JsonIndexOptions).IsNotNull();
		await Assert.That(options.JsonIndexOptions.Indexes).IsEmpty();
	}

	[Test]
	public async Task Constructor_GivenUnsupportedJsonIndexIncludeColumn_ThrowsArgumentException()
	{
		var options = CreateDefaultOptions();
		options.JsonIndexOptions = new SqlServerJsonIndexOptions
		{
			Enabled = true,
			Indexes =
			[
				new SqlServerJsonIndexDefinition { JsonPath = "$.StringProperty", IncludeColumns = ["Version"] },
			],
		};

		await Assert.That(() => CreateStore(options: options)).Throws<ArgumentException>();
	}

	[Test]
	public async Task Constructor_GivenJsonIndexWithDuplicateDefinition_ThrowsArgumentException()
	{
		var options = CreateDefaultOptions();
		options.JsonIndexOptions = new SqlServerJsonIndexOptions
		{
			Enabled = true,
			Indexes =
			[
				new SqlServerJsonIndexDefinition { JsonPath = "$.StringProperty", IncludeColumns = ["Id"] },
				new SqlServerJsonIndexDefinition { JsonPath = "$.StringProperty", IncludeColumns = ["Id"] },
			],
		};

		await Assert.That(() => CreateStore(options: options)).Throws<ArgumentException>();
	}

	[Test]
	public async Task Constructor_GivenJsonIndexWithConflictingComputedColumnName_ThrowsArgumentException()
	{
		var options = CreateDefaultOptions();
		options.JsonIndexOptions = new SqlServerJsonIndexOptions
		{
			Enabled = true,
			Indexes =
			[
				new SqlServerJsonIndexDefinition
				{
					JsonPath = "$.StringProperty",
					ComputedColumnName = "Json_Value",
					SqlType = "nvarchar(450)",
				},
				new SqlServerJsonIndexDefinition
				{
					JsonPath = "$.IncrementInt32",
					ComputedColumnName = "Json_Value",
					SqlType = "int",
				},
			],
		};

		await Assert.That(() => CreateStore(options: options)).Throws<ArgumentException>();
	}

	static SqlServerSnapshotEventStore<TestAggregate> CreateStore(
		INonQueryableEventStore<TestAggregate>? eventStore = null,
		SqlServerSnapshotEventStoreOptions? options = null,
		ISnapshotStrategy<TestAggregate>? snapshotStrategy = null,
		ISnapshotStrategySelector? snapshotStrategySelector = null
	)
	{
		eventStore ??= INonQueryableEventStore<TestAggregate>.Mock();
		var wrappedOptions = Options.Create(options ?? CreateDefaultOptions());
		var telemetry = TestHelpers.CreateSqlServerSnapshotEventStoreTelemetry();

		return new(eventStore, wrappedOptions, telemetry, snapshotStrategy, snapshotStrategySelector);
	}

	static SqlServerSnapshotEventStoreOptions CreateDefaultOptions() =>
		new()
		{
			ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=TestDb;Trusted_Connection=True;",
			TableName = "TestSnapshots",
			SchemaName = "dbo",
			AutoCreateTable = false,
		};
}
