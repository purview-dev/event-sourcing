using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Aggregates.Snapshotting;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.ValueObjects;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.SqlServer.Snapshot;
using Purview.EventSourcing.SqlServer.Snapshots;
using Testcontainers.MsSql;

namespace Purview.EventSourcing.Benchmarks.SqlServer;

/// <summary>
/// Measures event-store save/get and snapshot write/query timings against a SQL Server
/// Testcontainer. The container is started once in <see cref="GlobalSetup"/> and torn down in
/// <see cref="GlobalCleanup"/>; the benchmark methods perform idempotent or append-only
/// operations so repeated invocations remain valid.
/// </summary>
public class SqlServerPerformanceBenchmarks
{
	readonly SqlServerPerformanceWorkload _workload = new()
	{
		AggregateCount = 80,
		EventsPerAggregate = 16,
		QueryIterations = 40,
	};

	MsSqlContainer? _container;

	SqlServerEventStore<PersistenceAggregate>? _eventStore;

	SqlServerSnapshotEventStore<PersistenceAggregate>? _snapshotStore;

	SqlServerSnapshotEventStore<CustomerAggregate>? _customerStore;

	SqlServerSnapshotEventStore<SnapshotValueObjectsAggregate>? _valueObjectStore;

	string[] _seededIds = [];

	PersistenceAggregate[] _loadedAggregates = [];

	int _saveCounter;

	int _getIndex;

	int _snapshotIndex;

	bool _previousRequiresPrincipal;

	[GlobalSetup]
	public void GlobalSetup()
	{
		_previousRequiresPrincipal = EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault;
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;

		_container = ContainerHelper.CreateMsSql();
		_container.StartAsync().GetAwaiter().GetResult();

		var connectionString = _container.GetConnectionString();
		var runId = Guid.NewGuid().ToString("N");

		_eventStore = CreateEventStore(connectionString, $"PerfEvents_{runId}");
		_snapshotStore = CreateSnapshotStore(_eventStore, connectionString, $"PerfSnapshots_{runId}");

		_seededIds = [.. Enumerable.Range(0, _workload.AggregateCount).Select(static _ => $"{Guid.NewGuid():D}")];

		var loaded = new List<PersistenceAggregate>(_seededIds.Length);
		for (var i = 0; i < _seededIds.Length; i++)
		{
			var aggregate = _eventStore.CreateAsync(_seededIds[i]).GetAwaiter().GetResult();
			PopulateAggregate(aggregate, i, _workload.EventsPerAggregate);
			var result = _eventStore.SaveAsync(aggregate, operationContext: null).GetAwaiter().GetResult();
			if (!result.Saved || !result.IsValid)
				throw new InvalidOperationException($"Seeding save failed for aggregate '{_seededIds[i]}'.");

			var loadedAggregate =
				_eventStore.GetAsync(_seededIds[i], operationContext: null).GetAwaiter().GetResult()
				?? throw new InvalidOperationException($"Seeded aggregate '{_seededIds[i]}' was not found.");
			ValidateAggregate(loadedAggregate, i, _workload.EventsPerAggregate);
			loaded.Add(loadedAggregate);
		}

		_loadedAggregates = [.. loaded];

		foreach (var aggregate in _loadedAggregates)
			_snapshotStore.SnapshotAsync(aggregate).GetAwaiter().GetResult();

		var snapshotCount = _snapshotStore
			.CountAsync(static aggregate => aggregate.IncrementInt32 > 0)
			.GetAwaiter()
			.GetResult();
		if (snapshotCount != _workload.AggregateCount)
		{
			throw new InvalidOperationException(
				$"Snapshot count mismatch. Expected {_workload.AggregateCount}, got {snapshotCount}."
			);
		}

		var complexTableSuffix = Guid.NewGuid().ToString("N");
		_customerStore = CreateCustomerSnapshotStore(
			connectionString,
			$"PerfComplexEvents_{complexTableSuffix}",
			$"PerfComplexSnap_{complexTableSuffix}"
		);
		_valueObjectStore = CreateSnapshotValueObjectsStore(
			connectionString,
			$"PerfValueObjectEvents_{complexTableSuffix}",
			$"PerfValueObjectSnap_{complexTableSuffix}"
		);

		const string matchingCustomerName = "complex customer";
		const string matchingCustomerEmail = "complex.customer@test.com";
		var expectedCustomerMatches = SeedCustomerAggregatesAsync(
				_customerStore,
				_workload.AggregateCount,
				matchingCustomerName,
				matchingCustomerEmail
			)
			.GetAwaiter()
			.GetResult();
		VerifyCustomerSnapshotCountAsync(
				_customerStore,
				matchingCustomerName,
				matchingCustomerEmail,
				expectedCustomerMatches
			)
			.GetAwaiter()
			.GetResult();

		const string matchingDisplayName = "snapshot-user";
		const string matchingDisplayName2Prefix = "snapshot-v2-";
		var matchingUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
		var expectedValueObjectMatches = SeedValueObjectAggregatesAsync(
				_valueObjectStore,
				_workload.AggregateCount,
				matchingDisplayName,
				matchingDisplayName2Prefix,
				matchingUserId
			)
			.GetAwaiter()
			.GetResult();
		VerifyValueObjectSnapshotCountAsync(
				_valueObjectStore,
				matchingUserId,
				matchingDisplayName,
				matchingDisplayName2Prefix,
				expectedValueObjectMatches
			)
			.GetAwaiter()
			.GetResult();
	}

	[GlobalCleanup]
	public async Task GlobalCleanup()
	{
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = _previousRequiresPrincipal;

		if (_container is not null)
			await _container.DisposeAsync();
	}

	[Benchmark]
	[BenchmarkCategory("EventStore")]
	public async Task EventStore_Save()
	{
		var aggregate = await _eventStore!.CreateAsync($"save-{_saveCounter++}");
		PopulateAggregate(aggregate, _saveCounter, _workload.EventsPerAggregate);

		var result = await _eventStore.SaveAsync(aggregate, operationContext: null);
		if (!result.Saved || !result.IsValid)
			throw new InvalidOperationException($"Save failed for aggregate '{aggregate.Id()}'.");
	}

	[Benchmark]
	[BenchmarkCategory("EventStore")]
	public async Task EventStore_Save_NoSnapshot()
	{
		var aggregate = await _eventStore!.CreateAsync($"save-ns-{_saveCounter++}");
		PopulateAggregate(aggregate, _saveCounter, _workload.EventsPerAggregate);

		var operationContext = EventStoreOperationContext.DefaultContext();
		operationContext.SetSnapshotStrategy(new IntervalSnapshotStrategy<PersistenceAggregate>(1000));

		var result = await _eventStore.SaveAsync(aggregate, operationContext);
		if (!result.Saved || !result.IsValid)
			throw new InvalidOperationException($"Save failed for aggregate '{aggregate.Id()}'.");
	}

	[Benchmark]
	[BenchmarkCategory("EventStore")]
	public async Task EventStore_Get()
	{
		var index = _getIndex++ % _seededIds.Length;
		var aggregate =
			await _eventStore!.GetAsync(_seededIds[index], operationContext: null)
			?? throw new InvalidOperationException($"Aggregate '{_seededIds[index]}' was not found.");

		ValidateAggregate(aggregate, index, _workload.EventsPerAggregate);
	}

	[Benchmark]
	[BenchmarkCategory("SnapshotStore")]
	public Task SnapshotStore_Snapshot() =>
		_snapshotStore!.SnapshotAsync(_loadedAggregates[_snapshotIndex++ % _loadedAggregates.Length]);

	[Benchmark]
	[BenchmarkCategory("SnapshotStore")]
	public async Task SnapshotStore_Query()
	{
		var page = await _snapshotStore!.QueryAsync(
			static aggregate => aggregate.IncrementInt32 > 0,
			static queryable => queryable.OrderBy(aggregate => aggregate.Int32Value),
			new ContinuationRequest { MaxRecords = 25, IncludeTotalCount = true }
		);

		if (page.Results.Length == 0)
			throw new InvalidOperationException("Snapshot query returned no results.");
	}

	[Benchmark]
	[BenchmarkCategory("SnapshotStore")]
	public async Task SnapshotStore_Query_ComplexAggregate()
	{
		const string matchingCustomerName = "complex customer";
		const string matchingCustomerEmail = "complex.customer@test.com";
		var customerResult = await _customerStore!.QueryAsync(
			aggregate =>
				aggregate.IsActive
				&& (aggregate.Name == matchingCustomerName || aggregate.Email == matchingCustomerEmail),
			queryable => queryable.OrderBy(aggregate => aggregate.Name).ThenBy(aggregate => aggregate.Email),
			new ContinuationRequest { MaxRecords = 20, IncludeTotalCount = true }
		);
		if (customerResult.Results.Length == 0)
			throw new InvalidOperationException("Complex scalar snapshot query returned no results.");

		var matchingUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
		const string matchingDisplayName = "snapshot-user";
		const string matchingDisplayName2Prefix = "snapshot-v2-";
		var valueObjectResult = await _valueObjectStore!.QueryAsync(
			aggregate =>
				aggregate.UserDetails.Id == matchingUserId
				&& aggregate.UserDetails.IsActive
				&& aggregate.UserDetails.DisplayName == matchingDisplayName
				&& aggregate.UserDetails2.DisplayName.StartsWith(matchingDisplayName2Prefix),
			queryable =>
				queryable
					.OrderBy(aggregate => aggregate.UserDetails.DisplayName)
					.ThenBy(aggregate => aggregate.UserDetails2.DisplayName),
			new ContinuationRequest { MaxRecords = 20, IncludeTotalCount = true }
		);
		if (valueObjectResult.Results.Length == 0)
			throw new InvalidOperationException("Complex value-object snapshot query returned no results.");
	}

	async Task<int> SeedCustomerAggregatesAsync(
		SqlServerSnapshotEventStore<CustomerAggregate> customerStore,
		int customerAggregateCount,
		string matchingCustomerName,
		string matchingCustomerEmail
	)
	{
		var expectedCustomerMatches = 0;

		for (var i = 0; i < customerAggregateCount; i++)
		{
			var matches = i % 3 == 0;
			if (matches)
				expectedCustomerMatches++;

			CustomerAggregate aggregate = new() { Details = { Id = $"{Guid.NewGuid():D}" } };
			aggregate.RegisterCustomer(
				matches ? matchingCustomerName : $"other customer {i}",
				matches ? matchingCustomerEmail : $"other-{i}@test.com",
				isActive: i % 2 == 0
			);

			if (matches)
				aggregate.Reactivate();
			else if (i % 2 != 0)
				aggregate.Deactivate();

			await customerStore.SnapshotAsync(aggregate);
		}

		return expectedCustomerMatches;
	}

	async Task VerifyCustomerSnapshotCountAsync(
		SqlServerSnapshotEventStore<CustomerAggregate> customerStore,
		string matchingCustomerName,
		string matchingCustomerEmail,
		int expectedCustomerMatches
	)
	{
		var customerMatchCount = await customerStore.CountAsync(aggregate =>
			aggregate.IsActive && (aggregate.Name == matchingCustomerName || aggregate.Email == matchingCustomerEmail)
		);
		if (customerMatchCount != expectedCustomerMatches)
		{
			throw new InvalidOperationException(
				$"Customer snapshot count mismatch. Expected {expectedCustomerMatches}, got {customerMatchCount}."
			);
		}
	}

	async Task<int> SeedValueObjectAggregatesAsync(
		SqlServerSnapshotEventStore<SnapshotValueObjectsAggregate> valueObjectStore,
		int valueObjectAggregateCount,
		string matchingDisplayName,
		string matchingDisplayName2Prefix,
		Guid matchingUserId
	)
	{
		var expectedValueObjectMatches = 0;

		for (var i = 0; i < valueObjectAggregateCount; i++)
		{
			var matches = i % 2 == 0;
			if (matches)
				expectedValueObjectMatches++;

			SnapshotValueObjectsAggregate aggregate = new() { Details = { Id = $"{Guid.NewGuid():D}" } };
			var userId = matches ? matchingUserId : Guid.Parse("44444444-4444-4444-4444-444444444444");

			aggregate.CaptureUserDetails(
				UserDetails.Create(userId, matches ? matchingDisplayName : $"other-user-{i}", true),
				UserDetails2.Create(userId, matches ? $"{matchingDisplayName2Prefix}{i}" : $"other-v2-{i}")
			);

			await valueObjectStore.SnapshotAsync(aggregate);
		}

		return expectedValueObjectMatches;
	}

	async Task VerifyValueObjectSnapshotCountAsync(
		SqlServerSnapshotEventStore<SnapshotValueObjectsAggregate> valueObjectStore,
		Guid matchingUserId,
		string matchingDisplayName,
		string matchingDisplayName2Prefix,
		int expectedValueObjectMatches
	)
	{
		var valueObjectMatchCount = await valueObjectStore.CountAsync(aggregate =>
			aggregate.UserDetails.Id == matchingUserId
			&& aggregate.UserDetails.IsActive
			&& aggregate.UserDetails.DisplayName == matchingDisplayName
			&& aggregate.UserDetails2.DisplayName.StartsWith(matchingDisplayName2Prefix)
		);
		if (valueObjectMatchCount != expectedValueObjectMatches)
		{
			throw new InvalidOperationException(
				$"Value-object snapshot count mismatch. Expected {expectedValueObjectMatches}, got {valueObjectMatchCount}."
			);
		}
	}

	static SqlServerSnapshotEventStore<CustomerAggregate> CreateCustomerSnapshotStore(
		string connectionString,
		string eventTableName,
		string tableName
	)
	{
		var eventStore = CreateEventStore<CustomerAggregate>(connectionString, eventTableName);

		return new SqlServerSnapshotEventStore<CustomerAggregate>(
			eventStore,
			Options.Create(
				new SqlServerSnapshotEventStoreOptions
				{
					ConnectionString = connectionString,
					TableName = tableName,
					SchemaName = "dbo",
					AutoCreateTable = true,
				}
			),
			NoOpProxyFactory.Create<ISqlServerSnapshotEventStoreTelemetry>()
		);
	}

	static SqlServerSnapshotEventStore<SnapshotValueObjectsAggregate> CreateSnapshotValueObjectsStore(
		string connectionString,
		string eventTableName,
		string tableName
	)
	{
		var eventStore = CreateEventStore<SnapshotValueObjectsAggregate>(connectionString, eventTableName);

		return new SqlServerSnapshotEventStore<SnapshotValueObjectsAggregate>(
			eventStore,
			Options.Create(
				new SqlServerSnapshotEventStoreOptions
				{
					ConnectionString = connectionString,
					TableName = tableName,
					SchemaName = "dbo",
					AutoCreateTable = true,
				}
			),
			NoOpProxyFactory.Create<ISqlServerSnapshotEventStoreTelemetry>()
		);
	}

	static SqlServerEventStore<PersistenceAggregate> CreateEventStore(string connectionString, string tableName)
	{
		return CreateEventStore<PersistenceAggregate>(connectionString, tableName);
	}

	static SqlServerEventStore<TAggregate> CreateEventStore<TAggregate>(string connectionString, string tableName)
		where TAggregate : class, IAggregate, new()
	{
		SqlServerEventStoreOptions options = new()
		{
			ConnectionString = connectionString,
			TableName = tableName,
			SchemaName = "dbo",
			AutoCreateTable = true,
			TimeoutInSeconds = 120,
			CacheMode = SnapshotCachingOptions.None,
			RequiresValidPrincipalIdentifier = false,
		};

		return new SqlServerEventStore<TAggregate>(
			eventNameMapper: new PerformanceAggregateEventNameMapper(),
			sqlServerOptions: Options.Create(options),
			distributedCache: new NoOpDistributedCache(),
			eventStoreTelemetry: NoOpProxyFactory.Create<ISqlServerEventStoreTelemetry>(),
			aggregateChangeNotifier: new NoOpAggregateChangeFeedNotifier<TAggregate>(),
			aggregateRequirementsManager: new NoOpAggregateRequirementsManager()
		);
	}

	static SqlServerSnapshotEventStore<PersistenceAggregate> CreateSnapshotStore(
		SqlServerEventStore<PersistenceAggregate> eventStore,
		string connectionString,
		string tableName
	)
	{
		return new SqlServerSnapshotEventStore<PersistenceAggregate>(
			eventStore,
			Options.Create(
				new SqlServerSnapshotEventStoreOptions
				{
					ConnectionString = connectionString,
					TableName = tableName,
					SchemaName = "dbo",
					AutoCreateTable = true,
				}
			),
			NoOpProxyFactory.Create<ISqlServerSnapshotEventStoreTelemetry>()
		);
	}

	static void PopulateAggregate(PersistenceAggregate aggregate, int sequence, int eventsPerAggregate)
	{
		aggregate.SetInt32Value(sequence);
		for (var i = 0; i < eventsPerAggregate; i++)
		{
			aggregate.IncrementInt32Value();
			aggregate.AppendString($"ev-{sequence}-{i}|");
		}
	}

	static void ValidateAggregate(PersistenceAggregate aggregate, int sequence, int eventsPerAggregate)
	{
		if (aggregate.Int32Value != sequence)
			throw new InvalidOperationException($"Int32Value mismatch for aggregate '{aggregate.Id()}'.");

		if (aggregate.IncrementInt32 != eventsPerAggregate)
			throw new InvalidOperationException($"IncrementInt32 mismatch for aggregate '{aggregate.Id()}'.");

		if (string.IsNullOrWhiteSpace(aggregate.StringProperty))
			throw new InvalidOperationException(
				$"StringProperty should not be empty for aggregate '{aggregate.Id()}'."
			);
	}

	sealed class PerformanceAggregateEventNameMapper : IAggregateEventNameMapper
	{
		public string GetName<T>(object aggregateEvent)
			where T : IAggregate => GetName<T>(aggregateEvent.GetType());

		public string GetName<T>(Type aggregateEventType)
			where T : IAggregate =>
			aggregateEventType.AssemblyQualifiedName ?? aggregateEventType.FullName ?? aggregateEventType.Name;

		public string? GetTypeName<T>(string eventTypeName)
			where T : IAggregate => string.IsNullOrWhiteSpace(eventTypeName) ? null : eventTypeName;

		public string InitializeAggregate<T>()
			where T : class, IAggregate, new() => new T().AggregateType;
	}

	sealed class NoOpAggregateRequirementsManager : IAggregateRequirementsManager
	{
		public void Fulfil(IAggregate aggregate) { }
	}

	sealed class NoOpAggregateChangeFeedNotifier<TAggregate> : IAggregateChangeFeedNotifier<TAggregate>
		where TAggregate : class, IAggregate, new()
	{
		public Task BeforeSaveAsync(TAggregate aggregate, bool isNew, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task BeforeDeleteAsync(TAggregate aggregate, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task AfterSaveAsync(
			TAggregate aggregate,
			int previousSavedVersion,
			bool isNew,
			EventRecord[] events,
			CancellationToken cancellationToken = default
		) => Task.CompletedTask;

		public Task AfterDeleteAsync(TAggregate aggregate, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task FailureAsync(
			TAggregate aggregate,
			bool isDelete,
			Exception exception,
			CancellationToken cancellationToken = default
		) => Task.CompletedTask;
	}

	sealed class NoOpDistributedCache : IDistributedCache
	{
		public byte[]? Get(string key) => null;

		public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);

		public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }

		public Task SetAsync(
			string key,
			byte[] value,
			DistributedCacheEntryOptions options,
			CancellationToken token = default
		) => Task.CompletedTask;

		public void Refresh(string key) { }

		public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

		public void Remove(string key) { }

		public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
	}

	static class NoOpProxyFactory
	{
		public static T Create<T>()
			where T : class => DispatchProxy.Create<T, NoOpDispatchProxy>();
	}

	sealed class NoOpDispatchProxy : DispatchProxy
	{
		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			if (targetMethod is null)
				return null;

			var returnType = targetMethod.ReturnType;

			if (returnType == typeof(void))
				return null;

			if (returnType == typeof(Task))
				return Task.CompletedTask;

			if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
			{
				var genericType = returnType.GetGenericArguments()[0];
				var value = genericType.IsValueType ? Activator.CreateInstance(genericType) : null;
				var method = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(genericType);
				return method.Invoke(null, [value]);
			}

			return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
		}
	}
}
