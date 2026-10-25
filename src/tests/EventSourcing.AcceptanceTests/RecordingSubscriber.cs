using System.Collections.Concurrent;
using Purview.EventSourcing.Subscriptions;

namespace Purview.EventSourcing;

/// <summary>
/// What a subscriber was handed, and for which tenant.
/// </summary>
/// <param name="TenantId">The tenant the subscription was running as; null for the system scope.</param>
/// <param name="AggregateId">The aggregate the event belonged to.</param>
/// <param name="AggregateVersion">The version the event was recorded at.</param>
public sealed record DeliveredEvent(string? TenantId, string AggregateId, long AggregateVersion);

/// <summary>
/// An <see cref="IEventSubscriber{T}"/> that records what it receives, so a scenario can assert on it.
/// </summary>
public sealed class RecordingSubscriber : IEventSubscriber<AcceptanceAggregate>
{
	/// <inheritdoc/>
	public string SubscriptionName => "acceptance";

	/// <summary>Every event handed to the subscriber, in delivery order.</summary>
	public ConcurrentQueue<DeliveredEvent> Delivered { get; } = new();

	/// <summary>When set, the subscriber throws for any event this returns true for.</summary>
	public Func<EventSubscriptionContext<AcceptanceAggregate>, bool>? FailOn { get; set; }

	/// <inheritdoc/>
	public Task OnEventAsync(EventSubscriptionContext<AcceptanceAggregate> context, CancellationToken cancellationToken)
	{
		if (FailOn?.Invoke(context) == true)
			throw new InvalidOperationException($"Subscriber refused version {context.AggregateVersion}.");

		Delivered.Enqueue(new(context.Scope.TenantId, context.AggregateId, context.AggregateVersion));

		return Task.CompletedTask;
	}
}
