namespace Purview.EventSourcing.Failures;

/// <summary>
/// The write lost an optimistic-concurrency check: the stream moved on after the aggregate was read.
/// </summary>
/// <param name="AggregateType">The aggregate type name.</param>
/// <param name="AggregateId">The identifier of the aggregate.</param>
/// <param name="ExpectedVersion">The stream version the write expected.</param>
/// <param name="ActualVersion">The stream version that was found.</param>
/// <remarks>
/// This is the case <see cref="ConcurrencyRetry"/> recognizes, and the one a caller re-reads and retries on.
/// </remarks>
public readonly record struct ConcurrencyConflict(
	string AggregateType,
	string AggregateId,
	long ExpectedVersion,
	long ActualVersion
);
