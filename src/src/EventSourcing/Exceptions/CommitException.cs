namespace Purview.EventSourcing.Exceptions;

/// <summary>
/// Thrown when events could not be committed for a reason other than optimistic concurrency.
/// </summary>
/// <remarks>
/// One type for every provider. Providers that surface a transport status - Azure Table Storage, for
/// example - supply it through <see cref="ErrorCode"/> and <see cref="StatusMessage"/>; the rest attach the
/// underlying failure as the inner exception.
/// </remarks>
#pragma warning disable CA1032 // Implement standard exception constructors
public class CommitException : Exception
#pragma warning restore CA1032 // Implement standard exception constructors
{
	/// <summary>
	/// Creates a commit failure that wraps the underlying storage exception.
	/// </summary>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="idempotencyId">The idempotency id associated with the commit.</param>
	/// <param name="versionAttempted">The aggregate version that was attempted.</param>
	/// <param name="version">The aggregate version present in the store.</param>
	/// <param name="innerException">The underlying storage failure.</param>
	public CommitException(
		string aggregateId,
		string idempotencyId,
		long versionAttempted,
		long version,
		Exception? innerException = null
	)
		: base(
			$"Failed to commit events.\n\tAggregateId: {aggregateId}\n\tIdempotencyId: {idempotencyId}\n\tVersionAttempted: {versionAttempted}\n\tVersionPresent: {version}",
			innerException
		)
	{
		AggregateId = aggregateId ?? throw new ArgumentNullException(nameof(aggregateId));
		IdempotencyId = idempotencyId ?? throw new ArgumentNullException(nameof(idempotencyId));
		VersionAttempted = versionAttempted;
		Version = version;
	}

	/// <summary>
	/// Creates a commit failure that carries the transport status returned by the storage operation.
	/// </summary>
	/// <param name="errorCode">The status code returned by the storage operation.</param>
	/// <param name="aggregateId">The id of the aggregate.</param>
	/// <param name="idempotencyId">The idempotency id associated with the commit.</param>
	/// <param name="versionAttempted">The aggregate version that was attempted.</param>
	/// <param name="version">The aggregate version present in the store.</param>
	/// <param name="statusMessage">The status message returned by the storage operation.</param>
	public CommitException(
		int errorCode,
		string aggregateId,
		string idempotencyId,
		long versionAttempted,
		long version,
		string statusMessage
	)
		: base(
			$"Failed to commit events.\n\tErrorCode: {errorCode} - {statusMessage}\n\tAggregateId: {aggregateId}\n\tIdempotencyId: {idempotencyId}\n\tVersionAttempted: {versionAttempted}\n\tVersionPresent: {version}"
		)
	{
		AggregateId = aggregateId ?? throw new ArgumentNullException(nameof(aggregateId));
		IdempotencyId = idempotencyId ?? throw new ArgumentNullException(nameof(idempotencyId));
		VersionAttempted = versionAttempted;
		Version = version;
		ErrorCode = errorCode;
		StatusMessage = statusMessage;
	}

	/// <summary>
	/// Gets the id of the aggregate.
	/// </summary>
	public string AggregateId { get; }

	/// <summary>
	/// Gets the idempotency id associated with the commit.
	/// </summary>
	public string IdempotencyId { get; }

	/// <summary>
	/// Gets the aggregate version that was attempted.
	/// </summary>
	public long VersionAttempted { get; }

	/// <summary>
	/// Gets the aggregate version present in the store.
	/// </summary>
	public long Version { get; }

	/// <summary>
	/// Gets the status code returned by the storage operation, when the provider surfaces one.
	/// </summary>
	public int? ErrorCode { get; }

	/// <summary>
	/// Gets the status message returned by the storage operation, when the provider surfaces one.
	/// </summary>
	public string? StatusMessage { get; }
}
