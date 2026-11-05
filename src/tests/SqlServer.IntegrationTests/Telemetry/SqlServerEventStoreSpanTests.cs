using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates.Persistence;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.SqlServer.Events;

namespace Purview.EventSourcing.SqlServer.Telemetry;

/// <summary>
/// That the event store actually emits the spans its telemetry contract describes.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in the suite uses the telemetry <em>mock</em>, which records that a method was
/// called and returns no <see cref="Activity"/>. That proves the store calls telemetry; it proves
/// nothing about whether a span reaches a listener, because a listener attached to a mocked store
/// records nothing and every assertion written against it passes vacuously.
/// </para>
/// <para>
/// <strong>Span names are a contract.</strong> Dashboards, alerts and sampling rules are written
/// against them, and renaming one is a breaking change that no compiler and - until now - no test
/// would catch.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class SqlServerEventStoreSpanTests(SqlServerEventStoreFixture fixture)
{
	[Test]
	public async Task SaveAsync_EmitsTheSaveAggregateSpan(CancellationToken cancellationToken)
	{
		using SpanRecorder recorder = new();
		var store = CreateStoreWithRealTelemetry();

		var aggregate = await store.CreateAsync($"{Guid.NewGuid():N}", cancellationToken);
		aggregate.IncrementInt32Value();

		await store.SaveAsync(aggregate, cancellationToken);

		await Assert.That(recorder.Names).Contains("SaveAggregate");
	}

	[Test]
	public async Task GetAsync_EmitsTheGetAggregateSpan(CancellationToken cancellationToken)
	{
		var store = CreateStoreWithRealTelemetry();
		var aggregateId = $"{Guid.NewGuid():N}";

		var aggregate = await store.CreateAsync(aggregateId, cancellationToken);
		aggregate.IncrementInt32Value();
		await store.SaveAsync(aggregate, cancellationToken);

		// The listener starts after the save, so the save's own spans cannot satisfy this.
		using SpanRecorder recorder = new();

		await store.GetAsync(aggregateId, cancellationToken: cancellationToken);

		await Assert.That(recorder.Names).Contains("GetAggregate");
	}

	// A soft delete appends a Deleted event through the ordinary save path, so it is traced as a
	// save. Worth asserting rather than assuming: a reader of the telemetry contract would expect
	// DeleteAsync to produce DeleteAggregate, and an alert written on that assumption never fires.
	[Test]
	public async Task DeleteAsync_SoftDeleting_IsTracedAsASave(CancellationToken cancellationToken)
	{
		var store = CreateStoreWithRealTelemetry();
		var aggregate = await SavedAggregateAsync(store, cancellationToken);

		using SpanRecorder recorder = new();

		await store.DeleteAsync(aggregate, cancellationToken: cancellationToken);

		await Assert.That(recorder.Names).Contains("SaveAggregate");
		await Assert.That(recorder.Names).DoesNotContain("DeleteAggregate");
	}

	[Test]
	public async Task DeleteAsync_PermanentlyDeleting_EmitsTheDeleteAggregateSpan(CancellationToken cancellationToken)
	{
		var store = CreateStoreWithRealTelemetry();
		var aggregate = await SavedAggregateAsync(store, cancellationToken);

		using SpanRecorder recorder = new();

		await store.DeleteAsync(
			aggregate,
			new EventStoreOperationContext { PermanentlyDelete = true, RequiresValidPrincipalIdentifier = false },
			cancellationToken
		);

		await Assert.That(recorder.Names).Contains("DeleteAggregate");
	}

	static async Task<PersistenceAggregate> SavedAggregateAsync(
		IEventStoreCore<PersistenceAggregate> store,
		CancellationToken cancellationToken
	)
	{
		var aggregate = await store.CreateAsync($"{Guid.NewGuid():N}", cancellationToken);
		aggregate.IncrementInt32Value();
		await store.SaveAsync(aggregate, cancellationToken);

		return aggregate;
	}

	// The aggregate type travels as baggage so a trace can be filtered to one type. A span with the
	// right name and nothing on it is not much use to whoever is looking at the trace.
	[Test]
	public async Task SaveAsync_TagsTheSpanWithTheAggregateType(CancellationToken cancellationToken)
	{
		using SpanRecorder recorder = new();
		var store = CreateStoreWithRealTelemetry();

		var aggregate = await store.CreateAsync($"{Guid.NewGuid():N}", cancellationToken);
		aggregate.IncrementInt32Value();

		await store.SaveAsync(aggregate, cancellationToken);

		var span = recorder.Activities.First(activity => activity.OperationName == "SaveAggregate");

		await Assert
			.That(
				span.Baggage.Concat(span.Tags)
					.Any(pair => pair.Value?.Contains("PersistenceAggregate", StringComparison.Ordinal) == true)
			)
			.IsTrue();
	}

	IEventStoreCore<PersistenceAggregate> CreateStoreWithRealTelemetry()
	{
		ServiceCollection services = new();
		services.AddLogging();
		// The generated telemetry takes an IMeterFactory for its counters, so the metrics services
		// have to be present even though nothing here asserts a metric.
		services.AddMetrics();
		services.AddSqlServerEventStoreTelemetry();

		var telemetry = services.BuildServiceProvider().GetRequiredService<ISqlServerEventStoreTelemetry>();

		return fixture.CreateEventStoreWithRealTelemetry<PersistenceAggregate>(telemetry);
	}

	/// <summary>
	/// Records every activity the event store's source produces while it is alive.
	/// </summary>
	/// <remarks>
	/// <c>ActivityListener</c> is opt-in per source and samples nothing unless asked, so
	/// <see cref="ActivitySamplingResult.AllDataAndRecorded"/> is required - with the default, the
	/// activity is created but carries no tags and the baggage assertion fails for the wrong reason.
	/// </remarks>
	sealed class SpanRecorder : IDisposable
	{
		readonly ActivityListener _listener;
		readonly List<Activity> _activities = [];
		readonly Lock _gate = new();

		public SpanRecorder()
		{
			_listener = new ActivityListener
			{
				ShouldListenTo = source => source.Name.Contains("EventSourcing", StringComparison.OrdinalIgnoreCase),
				Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
				ActivityStarted = Record,
			};

			ActivitySource.AddActivityListener(_listener);
		}

		void Record(Activity activity)
		{
			lock (_gate)
				_activities.Add(activity);
		}

		public IReadOnlyList<Activity> Activities
		{
			get
			{
				lock (_gate)
					return [.. _activities];
			}
		}

		public IReadOnlyList<string> Names => [.. Activities.Select(activity => activity.OperationName)];

		public void Dispose() => _listener.Dispose();
	}
}
