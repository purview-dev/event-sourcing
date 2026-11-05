using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Subscriptions;

/// <summary>
/// An event a subscriber could not handle, after the configured number of attempts.
/// </summary>
/// <param name="SubscriptionName">The subscription that could not handle it.</param>
/// <param name="Scope">The tenant scope the delivery ran under.</param>
/// <param name="AggregateId">The aggregate whose stream the event belongs to.</param>
/// <param name="AggregateVersion">The version within that stream, which is what makes it replayable.</param>
/// <param name="EventType">The stored event type name.</param>
/// <param name="Attempts">How many deliveries were attempted before giving up.</param>
/// <param name="Error">The last failure, as a string - the exception itself does not survive a store.</param>
/// <remarks>
/// Deliberately identifies the event rather than carrying it. The stream is still the canonical
/// record and has not been altered; everything needed to fetch, inspect and redeliver the event is
/// here, and a sink that copied the payload would become a second, divergent copy of data the
/// framework has gone to some trouble to keep in one place - including data that crypto-shredding is
/// supposed to be able to erase.
/// </remarks>
public sealed record DeadLetteredEvent(
	string SubscriptionName,
	TenantScope Scope,
	string AggregateId,
	long AggregateVersion,
	string EventType,
	int Attempts,
	string Error
);
