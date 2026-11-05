namespace Purview.EventSourcing.Admin.Failures;

/// <summary>
/// The admin query found nothing for the requested aggregate.
/// </summary>
/// <param name="AggregateType">The aggregate type name that was queried.</param>
/// <param name="AggregateId">The identifier that was requested.</param>
/// <remarks>
/// Covers a stream that does not exist and one that holds no events within the requested bounds; both leave
/// the caller with nothing to show.
/// </remarks>
public readonly record struct AdminAggregateNotFound(string AggregateType, string AggregateId);
