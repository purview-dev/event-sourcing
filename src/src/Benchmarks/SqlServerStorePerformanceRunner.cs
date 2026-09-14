using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.ValueObjects;
using Purview.EventSourcing.Services;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.SqlServer.Snapshot;
using Purview.EventSourcing.SqlServer.Snapshots;

namespace Purview.EventSourcing.SqlServer;

sealed class SqlServerStorePerformanceRunner
{
	public Task<SqlServerStorePerformanceRun> RunQuickAsync(CancellationToken cancellationToken) =>
		RunAsync(
			mode: "Quick",
			workload: new SqlServerPerformanceWorkload
			{
				AggregateCount = 80,
				EventsPerAggregate = 16,
				QueryIterations = 40,
			},
			thresholds: new ScenarioThresholds(
				EventSaveAverageMs: 90,
				EventGetAverageMs: 30,
				SnapshotWriteAverageMs: 35,
				SnapshotQueryAverageMs: 40
			),
			cancellationToken
		);

	public Task<SqlServerStorePerformanceRun> RunBenchmarkAsync(CancellationToken cancellationToken) =>
		RunAsync(
			mode: "Benchmark",
			workload: new SqlServerPerformanceWorkload
			{
				AggregateCount = 250,
				EventsPerAggregate = 24,
				QueryIterations = 120,
			},
			thresholds: new ScenarioThresholds(
				EventSaveAverageMs: 120,
				EventGetAverageMs: 70,
				SnapshotWriteAverageMs: 80,
				SnapshotQueryAverageMs: 90
			),
			cancellationToken
		);

	async Task<SqlServerStorePerformanceRun> RunAsync(
		string mode,
		SqlServerPerformanceWorkload workload,
		ScenarioThresholds thresholds,
		CancellationToken cancellationToken
	)
	{
		var previousRequiresPrincipal = EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault;
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;

		await using var msSqlContainer = ContainerHelper.CreateMsSql();
		await msSqlContainer.StartAsync(cancellationToken);

		try
		{
			var aggregateIds = Enumerable
				.Range(0, workload.AggregateCount)
				.Select(static _ => $"{Guid.NewGuid():D}")
				.ToArray();
			var connectionString = msSqlContainer.GetConnectionString();

			var runId = Guid.NewGuid().ToString("N");
			var eventStore = CreateEventStore(connectionString, $"PerfEvents_{runId}");
			var snapshotStore = CreateSnapshotStore(eventStore, connectionString, $"PerfSnapshots_{runId}");

			var saveScenario = await MeasureAsync(
				"EventStore.Save",
				workload.AggregateCount,
				thresholds.EventSaveAverageMs,
				async cancellationToken =>
				{
					for (var i = 0; i < aggregateIds.Length; i++)
					{
						var aggregate = await eventStore.CreateAsync(aggregateIds[i], cancellationToken);
						PopulateAggregate(aggregate, i, workload.EventsPerAggregate);

						var result = await eventStore.SaveAsync(aggregate, operationContext: null, cancellationToken);
						if (!result.Saved || !result.IsValid)
							throw new InvalidOperationException($"Save failed for aggregate '{aggregateIds[i]}'.");
					}
				},
				cancellationToken
			);

			List<PersistenceAggregate> loadedAggregates = new(aggregateIds.Length);
			var getScenario = await MeasureAsync(
				"EventStore.Get",
				workload.AggregateCount,
				thresholds.EventGetAverageMs,
				async cancellationToken =>
				{
					loadedAggregates.Clear();
					for (var i = 0; i < aggregateIds.Length; i++)
					{
						var aggregate =
							await eventStore.GetAsync(aggregateIds[i], operationContext: null, cancellationToken)
							?? throw new InvalidOperationException($"Aggregate '{aggregateIds[i]}' was not found.");

						ValidateAggregate(aggregate, i, workload.EventsPerAggregate);
						loadedAggregates.Add(aggregate);
					}
				},
				cancellationToken
			);

			var snapshotWriteScenario = await MeasureAsync(
				"SnapshotStore.Snapshot",
				workload.AggregateCount,
				thresholds.SnapshotWriteAverageMs,
				async cancellationToken =>
				{
					foreach (var aggregate in loadedAggregates)
						await snapshotStore.SnapshotAsync(aggregate, cancellationToken);
				},
				cancellationToken
			);

			var count = await snapshotStore.CountAsync(
				static aggregate => aggregate.IncrementInt32 > 0,
				cancellationToken
			);
			if (count != workload.AggregateCount)
			{
				throw new InvalidOperationException(
					$"Snapshot count mismatch. Expected {workload.AggregateCount}, got {count}."
				);
			}

			var snapshotQueryScenario = await MeasureAsync(
				"SnapshotStore.Query",
				workload.QueryIterations,
				thresholds.SnapshotQueryAverageMs,
				async cancellationToken =>
				{
					var page = await snapshotStore.QueryAsync(
						static aggregate => aggregate.IncrementInt32 > 0,
						static queryable => queryable.OrderBy(aggregate => aggregate.Int32Value),
						new ContinuationRequest { MaxRecords = 25, IncludeTotalCount = true },
						cancellationToken
					);

					if (page.Results.Length == 0)
						throw new InvalidOperationException("Snapshot query returned no results.");

					if (page.TotalCount is not null && page.TotalCount < workload.AggregateCount)
					{
						throw new InvalidOperationException(
							$"Snapshot total count mismatch. Expected at least {workload.AggregateCount}, got {page.TotalCount}."
						);
					}
				},
				cancellationToken
			);

			var complexSnapshotQueryScenario = await RunComplexSnapshotQueryScenarioAsync(
				connectionString,
				$"PerfComplexSnapshots_{runId}",
				workload.QueryIterations,
				cancellationToken
			);

			return new SqlServerStorePerformanceRun
			{
				Mode = mode,
				TimestampUtc = DateTimeOffset.UtcNow,
				MachineName = Environment.MachineName,
				FrameworkDescription = RuntimeInformation.FrameworkDescription,
				Workload = workload,
				Scenarios =
				[
					saveScenario,
					getScenario,
					snapshotWriteScenario,
					snapshotQueryScenario,
					complexSnapshotQueryScenario,
				],
			};
		}
		finally
		{
			EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = previousRequiresPrincipal;
		}
	}

	async Task<SqlServerStorePerformanceScenarioRun> RunComplexSnapshotQueryScenarioAsync(
		string connectionString,
		string tableName,
		int queryIterations,
		CancellationToken cancellationToken = default
	)
	{
		var customerStore = CreateCustomerSnapshotStore(
			connectionString,
			$"{tableName}_CustomerEvents",
			$"{tableName}_Customers"
		);
		var valueObjectStore = CreateSnapshotValueObjectsStore(
			connectionString,
			$"{tableName}_SnapshotValueObjectEvents",
			$"{tableName}_ValueObjects"
		);

		const int customerAggregateCount = 90;
		const string matchingCustomerName = "complex customer";
		const string matchingCustomerEmail = "complex.customer@test.com";

		var expectedCustomerMatches = await SeedCustomerAggregatesAsync(
			customerStore,
			customerAggregateCount,
			matchingCustomerName,
			matchingCustomerEmail,
			cancellationToken
		);
		await VerifyCustomerSnapshotCountAsync(
			customerStore,
			matchingCustomerName,
			matchingCustomerEmail,
			expectedCustomerMatches,
			cancellationToken
		);

		const int valueObjectAggregateCount = 60;
		const string matchingDisplayName = "snapshot-user";
		const string matchingDisplayName2Prefix = "snapshot-v2-";
		var matchingUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

		var expectedValueObjectMatches = await SeedValueObjectAggregatesAsync(
			valueObjectStore,
			valueObjectAggregateCount,
			matchingDisplayName,
			matchingDisplayName2Prefix,
			matchingUserId,
			cancellationToken
		);
		await VerifyValueObjectSnapshotCountAsync(
			valueObjectStore,
			matchingUserId,
			matchingDisplayName,
			matchingDisplayName2Prefix,
			expectedValueObjectMatches,
			cancellationToken
		);

		// Valid up to this point, so run the test...
		return await MeasureAsync(
			"SnapshotStore.Query.ComplexAggregate",
			queryIterations * 2,
			maxAllowedAverageMilliseconds: 65,
			async token =>
			{
				var customerResult = await customerStore.QueryAsync(
					aggregate =>
						aggregate.IsActive
						&& (aggregate.Name == matchingCustomerName || aggregate.Email == matchingCustomerEmail),
					queryable => queryable.OrderBy(aggregate => aggregate.Name).ThenBy(aggregate => aggregate.Email),
					new ContinuationRequest { MaxRecords = 20, IncludeTotalCount = true },
					token
				);

				if (customerResult.Results.Length == 0)
					throw new InvalidOperationException("Complex scalar snapshot query returned no results.");

				if (customerResult.TotalCount is not null && customerResult.TotalCount != expectedCustomerMatches)
				{
					throw new InvalidOperationException(
						$"Complex scalar snapshot query total mismatch. Expected {expectedCustomerMatches}, got {customerResult.TotalCount}."
					);
				}

				var valueObjectResult = await valueObjectStore.QueryAsync(
					aggregate =>
						aggregate.UserDetails.Id == matchingUserId
						&& aggregate.UserDetails.IsActive
						&& aggregate.UserDetails.DisplayName == matchingDisplayName
						&& aggregate.UserDetails2.DisplayName.StartsWith(matchingDisplayName2Prefix),
					queryable =>
						queryable
							.OrderBy(aggregate => aggregate.UserDetails.DisplayName)
							.ThenBy(aggregate => aggregate.UserDetails2.DisplayName),
					new ContinuationRequest { MaxRecords = 20, IncludeTotalCount = true },
					token
				);

				if (valueObjectResult.Results.Length == 0)
					throw new InvalidOperationException("Complex value-object snapshot query returned no results.");

				if (
					valueObjectResult.TotalCount is not null
					&& valueObjectResult.TotalCount != expectedValueObjectMatches
				)
				{
					throw new InvalidOperationException(
						$"Complex value-object snapshot query total mismatch. Expected {expectedValueObjectMatches}, got {valueObjectResult.TotalCount}."
					);
				}
			},
			cancellationToken
		);
	}

	async Task<int> SeedCustomerAggregatesAsync(
		SqlServerSnapshotEventStore<CustomerAggregate> customerStore,
		int customerAggregateCount,
		string matchingCustomerName,
		string matchingCustomerEmail,
		CancellationToken cancellationToken
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

			await customerStore.SnapshotAsync(aggregate, cancellationToken);
		}

		return expectedCustomerMatches;
	}

	async Task VerifyCustomerSnapshotCountAsync(
		SqlServerSnapshotEventStore<CustomerAggregate> customerStore,
		string matchingCustomerName,
		string matchingCustomerEmail,
		int expectedCustomerMatches,
		CancellationToken cancellationToken
	)
	{
		var customerMatchCount = await customerStore.CountAsync(
			aggregate =>
				aggregate.IsActive
				&& (aggregate.Name == matchingCustomerName || aggregate.Email == matchingCustomerEmail),
			cancellationToken
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
		Guid matchingUserId,
		CancellationToken cancellationToken
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

			await valueObjectStore.SnapshotAsync(aggregate, cancellationToken);
		}

		return expectedValueObjectMatches;
	}

	async Task VerifyValueObjectSnapshotCountAsync(
		SqlServerSnapshotEventStore<SnapshotValueObjectsAggregate> valueObjectStore,
		Guid matchingUserId,
		string matchingDisplayName,
		string matchingDisplayName2Prefix,
		int expectedValueObjectMatches,
		CancellationToken cancellationToken
	)
	{
		var valueObjectMatchCount = await valueObjectStore.CountAsync(
			aggregate =>
				aggregate.UserDetails.Id == matchingUserId
				&& aggregate.UserDetails.IsActive
				&& aggregate.UserDetails.DisplayName == matchingDisplayName
				&& aggregate.UserDetails2.DisplayName.StartsWith(matchingDisplayName2Prefix),
			cancellationToken
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

	static async Task<SqlServerStorePerformanceScenarioRun> MeasureAsync(
		string name,
		int operationCount,
		double maxAllowedAverageMilliseconds,
		Func<CancellationToken, Task> operation,
		CancellationToken cancellationToken = default
	)
	{
		var stopwatch = Stopwatch.StartNew();
		await operation(cancellationToken);
		stopwatch.Stop();

		var totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
		var averageMilliseconds = operationCount <= 0 ? totalMilliseconds : totalMilliseconds / operationCount;
		var operationsPerSecond = totalMilliseconds <= 0 ? 0 : operationCount / (totalMilliseconds / 1000d);

		return new SqlServerStorePerformanceScenarioRun
		{
			Name = name,
			OperationCount = operationCount,
			TotalMilliseconds = totalMilliseconds,
			AverageMilliseconds = averageMilliseconds,
			OperationsPerSecond = operationsPerSecond,
			MaxAllowedAverageMilliseconds = maxAllowedAverageMilliseconds,
			Passed = averageMilliseconds <= maxAllowedAverageMilliseconds,
		};
	}

	sealed record ScenarioThresholds(
		double EventSaveAverageMs,
		double EventGetAverageMs,
		double SnapshotWriteAverageMs,
		double SnapshotQueryAverageMs
	);

	sealed class PerformanceAggregateEventNameMapper : IAggregateEventNameMapper
	{
		public string GetName<T>(IEvent aggregateEvent)
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
			IEvent[] events,
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

	class NoOpDispatchProxy : DispatchProxy
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
