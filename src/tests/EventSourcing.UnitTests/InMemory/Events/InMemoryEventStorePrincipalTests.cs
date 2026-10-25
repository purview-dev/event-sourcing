using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing.InMemory.Events;

/// <summary>
/// Guards that a save records the user from the registered <see cref="IEventStorePrincipalAccessor"/>.
/// </summary>
/// <remarks>
/// The accessor exists because the ambient <see cref="ClaimsPrincipal.Current"/> is null in a web host -
/// ASP.NET Core populates neither it nor <see cref="Thread.CurrentPrincipal"/> - so every save recorded a
/// null user, and the default <c>RequiresValidPrincipalIdentifier</c> could not be satisfied at all.
/// </remarks>
public sealed class InMemoryEventStorePrincipalTests
{
	const string UserId = "user-42";

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

	sealed class StubPrincipalAccessor(ClaimsPrincipal? principal) : IEventStorePrincipalAccessor
	{
		public ClaimsPrincipal? GetPrincipal() => principal;
	}

	static ServiceProvider CreateProvider(ClaimsPrincipal? principal)
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.TryAddSingleton<IEventStorePrincipalAccessor>(new StubPrincipalAccessor(principal));
		services.AddEventSourcing().AddInMemoryEventStore();
		return services.BuildServiceProvider();
	}

	static ClaimsPrincipal PrincipalWith(string claimType, string value) =>
		new(new ClaimsIdentity([new Claim(claimType, value)]));

	[Test]
	public async Task SaveAsync_GivenRegisteredPrincipalAccessor_RecordsTheUserAgainstTheEvent()
	{
		// Arrange
		EventStoreOperationContext context = new() { RequiresValidPrincipalIdentifier = true };
		using var provider = CreateProvider(PrincipalWith(context.ClaimIdentifier, UserId));
		var eventStore = provider.GetRequiredService<IEventStore>();

		var aggregate = await eventStore.CreateAsync<TestAggregate>("principal-1");
		aggregate.Increment(1);

		// Act
		var result = await eventStore.SaveAsync(aggregate, context);

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();

		List<(Aggregates.EventRecord EventRecord, string EventType)> events = [];
		await foreach (
			var @event in eventStore.GetEventRangeAsync<TestAggregate>("principal-1", 1, null, CancellationToken.None)
		)
			events.Add(@event);

		await Assert.That(events).IsNotEmpty();
		await Assert.That(events[0].EventRecord.Metadata.UserId).IsEqualTo(UserId);
	}

	[Test]
	public async Task SaveAsync_GivenNoPrincipalAndIdentifierRequired_Throws()
	{
		// Arrange
		using var provider = CreateProvider(principal: null);
		var eventStore = provider.GetRequiredService<IEventStore>();

		var aggregate = await eventStore.CreateAsync<TestAggregate>("principal-2");
		aggregate.Increment(1);

		// Act & Assert
		await Assert
			.That(async () =>
				await eventStore.SaveAsync(
					aggregate,
					new EventStoreOperationContext { RequiresValidPrincipalIdentifier = true }
				)
			)
			.Throws<NullReferenceException>();
	}
}
