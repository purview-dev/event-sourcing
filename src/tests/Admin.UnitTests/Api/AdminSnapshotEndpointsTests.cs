using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Services;

namespace Purview.EventSourcing.Admin.API;

public sealed class AdminSnapshotEndpointsTests
{
	[Test]
	public async Task SnapshotStatus_GivenMaterializedSnapshot_ReportsExists(CancellationToken cancellationToken)
	{
		StubQueryableEventStore store = new() { Snapshot = new PersistenceAggregate() };
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.ViewSnapshot = true,
			configureServices: services =>
			{
				services.AddSingleton<IEventStore>(store);
				services.AddSingleton<IQueryableEventStore>(store);
				services.AddSingleton<IAggregateTypeRegistry>(new StubTypeRegistry("order"));
			}
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1/snapshot", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		var root = json!.RootElement;
		await Assert.That(root.GetProperty("exists").GetBoolean()).IsTrue();
		await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
	}

	[Test]
	public async Task SnapshotStatus_GivenNoSnapshot_ReportsNotExists(CancellationToken cancellationToken)
	{
		StubQueryableEventStore store = new() { Snapshot = null };
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.ViewSnapshot = true,
			configureServices: services =>
			{
				services.AddSingleton<IEventStore>(store);
				services.AddSingleton<IQueryableEventStore>(store);
				services.AddSingleton<IAggregateTypeRegistry>(new StubTypeRegistry("order"));
			}
		);
		var client = host.Client;

		var response = await client.GetAsync("/admin/api/aggregates/order/order-1/snapshot", cancellationToken);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		await Assert.That(json!.RootElement.GetProperty("exists").GetBoolean()).IsFalse();
	}

	[Test]
	public async Task RebuildSnapshot_GivenAggregate_SavesFreshSnapshot(CancellationToken cancellationToken)
	{
		PersistenceAggregate aggregate = new();
		StubQueryableEventStore store = new() { Snapshot = aggregate };
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.RebuildSnapshot = true,
			configureServices: services =>
			{
				services.AddSingleton<IEventStore>(store);
				services.AddSingleton<IQueryableEventStore>(store);
				services.AddSingleton<IAggregateTypeRegistry>(new StubTypeRegistry("order"));
			}
		);
		var client = host.Client;

		var response = await client.PostAsync(
			"/admin/api/aggregates/order/order-1/snapshot/rebuild",
			content: null,
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(store.SaveCount).IsEqualTo(1);
		var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
		await Assert.That(json!.RootElement.GetProperty("rebuilt").GetBoolean()).IsTrue();
	}

	[Test]
	public async Task RebuildSnapshot_GivenUnregisteredAggregate_ReturnsNotFound(CancellationToken cancellationToken)
	{
		StubQueryableEventStore store = new() { Snapshot = new PersistenceAggregate() };
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.RebuildSnapshot = true,
			configureServices: services =>
			{
				services.AddSingleton<IEventStore>(store);
				services.AddSingleton<IQueryableEventStore>(store);
				services.AddSingleton<IAggregateTypeRegistry>(new StubTypeRegistry("other"));
			}
		);
		var client = host.Client;

		var response = await client.PostAsync(
			"/admin/api/aggregates/order/order-1/snapshot/rebuild",
			content: null,
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task RebuildSnapshot_GivenMissingQueryableStore_ReturnsNotFound(CancellationToken cancellationToken)
	{
		StubQueryableEventStore store = new() { Snapshot = new PersistenceAggregate() };
		await using var host = await AdminTestHost.CreateAsync(
			configureAdmin: static options => options.Features.RebuildSnapshot = true,
			configureServices: services =>
			{
				services.AddSingleton<IEventStore>(store);
				services.AddSingleton<IAggregateTypeRegistry>(new StubTypeRegistry("order"));
			}
		);
		var client = host.Client;

		var response = await client.PostAsync(
			"/admin/api/aggregates/order/order-1/snapshot/rebuild",
			content: null,
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task RebuildSnapshot_WhenFeatureDisabled_ReturnsNotFound(CancellationToken cancellationToken)
	{
		await using var host = await AdminTestHost.CreateAsync();
		var client = host.Client;

		var response = await client.PostAsync(
			"/admin/api/aggregates/order/order-1/snapshot/rebuild",
			content: null,
			cancellationToken
		);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	sealed class StubQueryableEventStore : IQueryableEventStore
	{
		public PersistenceAggregate? Snapshot { get; set; }

		public int SaveCount { get; private set; }

		public Task<T?> GetAsync<T>(
			string aggregateId,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => Task.FromResult(Snapshot as T);

		public Task<T?> FirstOrDefaultAsync<T>(
			Expression<Func<T, bool>> whereClause,
			Func<IQueryable<T>, IQueryable<T>>? orderByClause,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => Task.FromResult(Snapshot as T);

		public Task<SaveResult<T>> SaveAsync<T>(
			T aggregate,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new()
		{
			SaveCount++;
			return Task.FromResult(
				new SaveResult<T>(aggregate, Validation.ValidationResult.Success, saved: true, skipped: false)
			);
		}

		public Task<T> CreateAsync<T>(string? aggregateId = null, CancellationToken cancellationToken = default)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<T?> GetOrCreateAsync<T>(
			string? aggregateId,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<T?> GetAtAsync<T>(
			string aggregateId,
			int version,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<T?> GetDeletedAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<bool> IsDeletedAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<bool> DeleteAsync<T>(
			T aggregate,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<bool> RestoreAsync<T>(
			T aggregate,
			EventStoreOperationContext? operationContext,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public IAsyncEnumerable<string> GetAggregateIdsAsync<T>(
			bool includeDeleted,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<ExistsState> ExistsAsync<T>(string aggregateId, CancellationToken cancellationToken = default)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public T FulfilRequirements<T>(T aggregate)
			where T : class, IAggregate, new() => aggregate;

		public IAsyncEnumerable<(EventRecord EventRecord, string EventType)> GetEventRangeAsync<T>(
			string aggregateId,
			int versionFrom,
			int? versionTo,
			CancellationToken cancellationToken
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public IAsyncEnumerable<T> GetQueryEnumerableAsync<T>(
			Expression<Func<T, bool>> whereClause,
			Func<IQueryable<T>, IQueryable<T>>? orderByClause,
			int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public IAsyncEnumerable<T> GetListEnumerableAsync<T>(
			Func<IQueryable<T>, IQueryable<T>>? orderByClause,
			int maxRecordsPerIteration = ContinuationRequest.DefaultMaxRecords,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<ContinuationResponse<T>> QueryAsync<T>(
			Expression<Func<T, bool>> whereClause,
			Func<IQueryable<T>, IQueryable<T>>? orderByClause,
			ContinuationRequest request,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<ContinuationResponse<T>> ListAsync<T>(
			Func<IQueryable<T>, IQueryable<T>>? orderByClause,
			ContinuationRequest request,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<long> CountAsync<T>(
			Expression<Func<T, bool>>? whereClause,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();

		public Task<T?> SingleOrDefaultAsync<T>(
			Expression<Func<T, bool>> whereClause,
			CancellationToken cancellationToken = default
		)
			where T : class, IAggregate, new() => throw new NotSupportedException();
	}

	sealed class StubTypeRegistry(string resolvableName) : IAggregateTypeRegistry
	{
		public bool TryResolve(
			string aggregateTypeName,
			[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Type? aggregateType
		)
		{
			if (StringComparer.Ordinal.Equals(aggregateTypeName, resolvableName))
			{
				aggregateType = typeof(PersistenceAggregate);
				return true;
			}

			aggregateType = null;
			return false;
		}
	}
}
