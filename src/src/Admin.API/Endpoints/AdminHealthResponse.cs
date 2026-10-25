namespace Purview.EventSourcing.Admin.API.Endpoints;

/// <summary>
/// The operational health summary returned by the Admin portal.
/// </summary>
/// <param name="Status">One of <see cref="AdminHealthStatus"/>: whether storage responded to the probe, or <c>Unknown</c> when no probe is registered.</param>
/// <param name="TimestampUtc">The UTC time the summary was produced.</param>
/// <param name="TransactionGuarantee">The merged transaction guarantee.</param>
/// <param name="SupportsEventStreams">Whether event streams are persisted.</param>
/// <param name="SupportsQueries">Whether a queryable snapshot store is available.</param>
/// <param name="SupportsTransactionalOutbox">Whether the transactional outbox is supported.</param>
/// <param name="OperationalLimitations">Stable provider limitation identifiers.</param>
public sealed record AdminHealthResponse(
	string Status,
	DateTimeOffset TimestampUtc,
	EventStoreTransactionGuarantee TransactionGuarantee,
	bool SupportsEventStreams,
	bool SupportsQueries,
	bool SupportsTransactionalOutbox,
	IReadOnlyList<string> OperationalLimitations
);
