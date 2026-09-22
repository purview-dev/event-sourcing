using BenchmarkDotNet.Attributes;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.ChangeFeed;
using Purview.EventSourcing.InMemory.Events;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Services;

namespace Purview.EventSourcing.Benchmarks;

/// <summary>
/// The allocation-free reference implementation: measures the in-memory event store's save/get and
/// replay throughput so provider overhead can be compared against a zero-I/O baseline.
/// </summary>
[MemoryDiagnoser]
public class InMemoryPerformanceBenchmarks : IDisposable
{
	InMemoryEventStore<OrderAggregate> _store = null!;

	EventStoreOperationContext _operationContext = null!;

	readonly string[] _seededIds = new string[50];

	string _largeAggregateId = string.Empty;

	int _saveCounter;

	int _getIndex;

	[GlobalSetup]
	public void GlobalSetup()
	{
		_operationContext = new EventStoreOperationContext { RequiresValidPrincipalIdentifier = false };

		_store = new InMemoryEventStore<OrderAggregate>(
			new NoOpChangeFeedNotifier<OrderAggregate>(),
			new NoOpRequirementsManager()
		);

		for (var i = 0; i < _seededIds.Length; i++)
		{
			var id = $"seed-{i}";
			_seededIds[i] = id;
			OrderAggregate aggregate = new() { Details = { Id = id } };
			aggregate.CreateOrder("customer-1");
			_store.SaveAsync(aggregate, _operationContext).GetAwaiter().GetResult();
		}

		_largeAggregateId = "large";
		OrderAggregate large = new() { Details = { Id = _largeAggregateId } };
		large.CreateOrder("customer-1");
		for (var i = 0; i < 100; i++)
		{
			large.AddLineItem(
				new EventStoreList<OrderLineItem>([new OrderLineItem($"p{i}", $"Product {i}", 1, 1.99m)]),
				1.99m
			);
		}
		_store.SaveAsync(large, _operationContext).GetAwaiter().GetResult();
	}

	[Benchmark]
	[BenchmarkCategory("InMemoryStore")]
	public OrderAggregate SaveNewAggregate()
	{
		OrderAggregate aggregate = new() { Details = { Id = $"save-{_saveCounter++}" } };
		aggregate.CreateOrder("customer-1");
		_store.SaveAsync(aggregate, _operationContext).GetAwaiter().GetResult();
		return aggregate;
	}

	[Benchmark]
	[BenchmarkCategory("InMemoryStore")]
	public async Task<OrderAggregate?> GetCachedAggregate()
	{
		var id = _seededIds[_getIndex++ % _seededIds.Length];
		return await _store.GetAsync(id, null);
	}

	[Benchmark]
	[BenchmarkCategory("InMemoryStore")]
	public async Task<OrderAggregate?> ReplayLargeStream() =>
		await _store.GetAtAsync(_largeAggregateId, int.MaxValue, null);

	[GlobalCleanup]
	public void GlobalCleanup() => Dispose();

	/// <inheritdoc/>
	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Releases the in-memory store.
	/// </summary>
	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
			_store.Dispose();
	}

	sealed class NoOpChangeFeedNotifier<TAggregate> : IAggregateChangeFeedNotifier<TAggregate>
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

	sealed class NoOpRequirementsManager : IAggregateRequirementsManager
	{
		public void Fulfil(IAggregate aggregate) { }
	}
}
