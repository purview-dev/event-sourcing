using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Events;

namespace Purview.EventSourcing;

/// <summary>
/// Provides event-history enumeration over an <see cref="IEventStoreCore{T}"/>.
/// </summary>
[System.Diagnostics.DebuggerStepThrough]
public static class IEventStoreCoreHistoryExtensions
{
	const int MaxAllowedPageSize = 1000;

	// Keyset tokens are prefixed so they cannot be confused with the legacy integer
	// continuation tokens produced by earlier versions.
	const string KeysetTokenPrefix = "k";

	/// <summary>
	/// Enumerates the event history for the aggregate, honoring the paging, version, and time filters in
	/// <paramref name="request"/>.
	/// </summary>
	/// <typeparam name="T">The aggregate type.</typeparam>
	/// <param name="eventStore">The event store used as the root object.</param>
	/// <param name="aggregateId">The id of the aggregate whose history should be enumerated.</param>
	/// <param name="request">The paging and filtering options; when null, the defaults are used.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>A <see cref="ContinuationResponse{AggregateEventHistoryItem}"/> containing the requested history items
	/// and a continuation token when more results are available.</returns>
	/// <remarks>
	/// <para>
	/// Paging uses a keyset continuation: the token records the last returned aggregate version, so the next
	/// page resumes scanning from that version rather than re-reading the entire range. This keeps each page
	/// proportional to the page size rather than the total number of events in the stream.
	/// </para>
	/// <para>
	/// Continuation tokens returned by earlier versions (a plain integer offset) are still accepted and
	/// retain their original skip semantics for backward compatibility.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentException">Thrown when <paramref name="aggregateId"/> is null or whitespace.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="request"/> contains invalid paging or range values.</exception>
	/// <exception cref="NotSupportedException">Thrown when the configured store does not implement <see cref="IAggregateEventHistoryStoreCore{T}"/>.</exception>
	public static async Task<ContinuationResponse<AggregateEventHistoryItem>> GetEventHistoryAsync<T>(
		[NotNull] this IEventStoreCore<T> eventStore,
		string aggregateId,
		AggregateEventHistoryRequest? request = null,
		CancellationToken cancellationToken = default
	)
		where T : class, IAggregate, new()
	{
		ArgumentNullException.ThrowIfNull(eventStore);
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);

		request ??= new AggregateEventHistoryRequest();
		ValidateRequest(request);

		if (eventStore is not IAggregateEventHistoryStoreCore<T> historyStore)
		{
			throw new NotSupportedException(
				$"The configured event store for aggregate type '{typeof(T).FullName}' does not support event history enumeration."
			);
		}

		var effectiveMaxRecords = request.MaxRecords;
		var (legacyOffset, keysetVersion) = ParseContinuation(request.ContinuationToken);
		var versionFrom = request.FromVersion ?? 1;
		var versionTo = request.ToVersion;

		// Keyset paging resumes from the version after the last returned event, so each page
		// only scans the events it needs.
		var scanFromVersion = keysetVersion.HasValue ? Math.Max(versionFrom, keysetVersion.Value + 1) : versionFrom;

		List<AggregateEventHistoryItem> items = [];
		var matchedCount = 0;
		var hasMore = false;
		await foreach (
			var (eventRecord, eventType) in historyStore.GetEventRangeAsync(
				aggregateId,
				scanFromVersion,
				versionTo,
				cancellationToken
			)
		)
		{
			var metadata = eventRecord.Metadata;
			if (request.FromUtc.HasValue && metadata.When < request.FromUtc.Value)
				continue;

			if (request.ToUtc.HasValue && metadata.When > request.ToUtc.Value)
				continue;

			// Legacy offset tokens skip matched records rather than scanning from a keyset.
			if (matchedCount < legacyOffset)
			{
				matchedCount++;
				continue;
			}

			if (items.Count >= effectiveMaxRecords)
			{
				hasMore = true;
				break;
			}

			items.Add(ToHistoryItem<T>(aggregateId, eventType, eventRecord));
			matchedCount++;
		}

		var token = hasMore ? $"{KeysetTokenPrefix}{items[^1].AggregateVersion}" : null;

		return new ContinuationResponse<AggregateEventHistoryItem>
		{
			Results = [.. items],
			RequestedCount = effectiveMaxRecords,
			ContinuationToken = token,
		};
	}

	static AggregateEventHistoryItem ToHistoryItem<T>(string aggregateId, string eventType, EventRecord eventRecord)
		where T : class, IAggregate, new()
	{
		var @event = eventRecord.Event;
		var metadata = eventRecord.Metadata;
		var payload = @event is UnknownEvent unknown
			? unknown.Payload
			: JsonSerializer.Serialize(@event, @event.GetType());

		return new AggregateEventHistoryItem
		{
			AggregateId = aggregateId,
			AggregateType = typeof(T).Name,
			EventType = eventType,
			EventClrType = @event.GetType().FullName ?? @event.GetType().Name,
			SchemaVersion = metadata.SchemaVersion,
			AggregateVersion = metadata.AggregateVersion,
			When = metadata.When,
			IdempotencyId = metadata.IdempotencyId,
			UserId = metadata.UserId,
			CausationId = metadata.CausationId,
			CorrelationId = metadata.CorrelationId,
			IsUnknownEvent = @event is UnknownEvent,
			Payload = payload,
		};
	}

	static (int Offset, int? KeysetVersion) ParseContinuation(string? continuationToken)
	{
		if (string.IsNullOrWhiteSpace(continuationToken))
			return (0, null);

		// Keyset token: "k{lastVersion}".
		if (
			continuationToken.StartsWith(KeysetTokenPrefix, StringComparison.Ordinal)
			&& int.TryParse(
				continuationToken.AsSpan(KeysetTokenPrefix.Length),
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var version
			)
			&& version >= 0
		)
			return (0, version);

		// Legacy offset token: an integer count of matched records to skip.
		if (
			int.TryParse(continuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
			&& offset >= 0
		)
		{
			return (offset, null);
		}

		// Invalid token format.
		throw new ArgumentOutOfRangeException(
			nameof(continuationToken),
			continuationToken,
			"Continuation token must be a keyset token (k{version}) or a non-negative integer offset."
		);
	}

	static void ValidateRequest(AggregateEventHistoryRequest request)
	{
		if (request.MaxRecords is < 1 or > MaxAllowedPageSize)
		{
			throw new ArgumentOutOfRangeException(
				nameof(request),
				request.MaxRecords,
				$"{nameof(request.MaxRecords)} must be between 1 and {MaxAllowedPageSize}."
			);
		}

		if (request.FromVersion is < 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(request),
				request.FromVersion,
				$"{nameof(request.FromVersion)} must be greater than 0 when provided."
			);
		}

		if (request.ToVersion is < 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(request),
				request.ToVersion,
				$"{nameof(request.ToVersion)} must be greater than 0 when provided."
			);
		}

		if (
			request.FromVersion.HasValue
			&& request.ToVersion.HasValue
			&& request.ToVersion.Value < request.FromVersion.Value
		)
		{
			throw new ArgumentOutOfRangeException(
				nameof(request),
				request.ToVersion,
				$"{nameof(request.ToVersion)} ({request.ToVersion}) must be greater than or equal to {nameof(request.FromVersion)} ({request.FromVersion})."
			);
		}

		if (request.FromUtc.HasValue && request.ToUtc.HasValue && request.ToUtc.Value < request.FromUtc.Value)
		{
			throw new ArgumentOutOfRangeException(
				nameof(request),
				request.ToUtc,
				$"{nameof(request.ToUtc)} ({request.ToUtc}) must be greater than or equal to {nameof(request.FromUtc)} ({request.FromUtc})."
			);
		}
	}
}
