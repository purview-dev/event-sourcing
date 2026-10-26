using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.InMemory.Events;

/// <summary>
/// Guards that a save records the shared event-store instruments.
/// </summary>
/// <remarks>
/// Listens to the real <see cref="Meter"/> rather than a stub, because instrument and tag names are the
/// public contract a dashboard is built against: a test against a mock would not notice them changing.
/// </remarks>
// A MeterListener observes the whole process, so two of these running together would each record the
// other's measurements.
[NotInParallel]
public sealed class InMemoryEventStoreMetricsTests
{
	const string MeterName = "Purview.EventSourcing";

	[EventContract]
	sealed record ValueIncremented
	{
		public static int SchemaVersion => 1;

		public EventMetadata Metadata { get; init; }

		public int Amount { get; set; }
	}

	sealed class TestAggregate : AggregateBase
	{
		public int Value { get; private set; }

		protected override void RegisterEvents() => Register<ValueIncremented>(ev => Value += ev.Amount);

		public void Increment(int amount) => RecordAndApply(new ValueIncremented { Amount = amount });
	}

	sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, string?> Tags);

	static async Task<List<Measurement>> RecordAsync(Func<IEventStore, Task> action)
	{
		List<Measurement> measurements = [];

		ServiceCollection services = new();
		services.AddLogging();
		services.AddMetrics();
		services.AddEventSourcing().AddInMemoryEventStore();

		using var provider = services.BuildServiceProvider();

		using MeterListener listener = new()
		{
			InstrumentPublished = (instrument, l) =>
			{
				if (instrument.Meter.Name == MeterName)
					l.EnableMeasurementEvents(instrument);
			},
		};

		void Record<TValue>(
			Instrument instrument,
			TValue value,
			ReadOnlySpan<KeyValuePair<string, object?>> tags,
			object? _
		)
			where TValue : struct
		{
			Dictionary<string, string?> tagMap = [];
			foreach (var tag in tags)
				tagMap[tag.Key] = tag.Value?.ToString();

			lock (measurements)
				measurements.Add(
					new Measurement(
						instrument.Name,
						Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
						tagMap
					)
				);
		}

		listener.SetMeasurementEventCallback<double>(Record);
		listener.SetMeasurementEventCallback<long>(Record);
		listener.SetMeasurementEventCallback<int>(Record);
		listener.Start();

		await action(provider.GetRequiredService<IEventStore>());

		listener.Dispose();

		return measurements;
	}

	[Test]
	public async Task SaveAsync_GivenAWrite_RecordsDurationAndEventCount()
	{
		var measurements = await RecordAsync(async store =>
		{
			var aggregate = await store.CreateAsync<TestAggregate>("metrics-1");
			aggregate.Increment(1);
			aggregate.Increment(2);

			await store.SaveAsync(
				aggregate,
				new EventStoreOperationContext { RequiresValidPrincipalIdentifier = false }
			);
		});

		var save = measurements.SingleOrDefault(m => m.Instrument == "purview.eventsourcing.save.duration");
		await Assert.That(save).IsNotNull();
		await Assert.That(save!.Tags["outcome"]).IsEqualTo("written");
		await Assert.That(save.Tags["aggregate.type"]).IsEqualTo(nameof(TestAggregate));

		var appended = measurements.SingleOrDefault(m => m.Instrument == "purview.eventsourcing.events.appended");
		await Assert.That(appended).IsNotNull();
		await Assert.That(appended!.Value).IsEqualTo(2d);
	}

	[Test]
	public async Task SaveAsync_GivenNothingToSave_RecordsNoChangesAndNoEventCount()
	{
		var measurements = await RecordAsync(async store =>
		{
			var aggregate = await store.CreateAsync<TestAggregate>("metrics-2");

			await store.SaveAsync(
				aggregate,
				new EventStoreOperationContext { RequiresValidPrincipalIdentifier = false }
			);
		});

		var save = measurements.SingleOrDefault(m => m.Instrument == "purview.eventsourcing.save.duration");
		await Assert.That(save).IsNotNull();
		await Assert.That(save!.Tags["outcome"]).IsEqualTo("no-changes");

		// An empty save appended nothing, so the counter must stay silent rather than record a zero.
		await Assert.That(measurements.Any(m => m.Instrument == "purview.eventsourcing.events.appended")).IsFalse();
	}
}
