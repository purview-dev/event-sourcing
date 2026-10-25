using Purview.EventSourcing.Failures;
using Purview.EventSourcing.Samples.Domain;

namespace Purview.EventSourcing.Samples.Services;

/// <summary>
/// Demonstrates multi-aggregate coordination using a single event-store transaction.
/// Transfers stock from a source inventory aggregate to a physical destination location,
/// creating the destination inventory aggregate on demand and enlisting both aggregates in the
/// same transaction so the commit succeeds or fails atomically.
/// </summary>
public sealed class StockTransferService(IEventStoreTransactionFactory transactionFactory, IQueryableEventStore store)
	: IStockTransferService
{
	public async Task<StockTransferResult> TransferAsync(
		string sourceInventoryId,
		string destinationLocationId,
		int quantity,
		string reason,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceInventoryId);
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationLocationId);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
		ArgumentException.ThrowIfNullOrWhiteSpace(reason);

		var sourceResult = await store.GetAsync<InventoryAggregate>(sourceInventoryId, cancellationToken);
		if (sourceResult.IsFailure)
			return StockTransferResult.Fail("Source stock item not found.");

		var source = sourceResult.Value;
		if (string.Equals(source.LocationId, destinationLocationId, StringComparison.OrdinalIgnoreCase))
			return StockTransferResult.Fail("Source and destination locations must be different.");

		var sourceLocationResult = await store.GetAsync<LocationAggregate>(source.LocationId, cancellationToken);
		if (sourceLocationResult.IsFailure)
			return StockTransferResult.Fail($"Source location '{source.LocationId}' not found.");

		var destinationLocationResult = await store.GetAsync<LocationAggregate>(
			destinationLocationId,
			cancellationToken
		);
		if (destinationLocationResult.IsFailure)
			return StockTransferResult.Fail("Destination location not found.");

		var sourceLocation = sourceLocationResult.Value;
		var destinationLocation = destinationLocationResult.Value;

		if (source.AvailableQuantity < quantity)
			return StockTransferResult.Fail(
				$"Insufficient available stock at '{sourceLocation.LocationName}'. "
					+ $"Available: {source.AvailableQuantity}, requested: {quantity}."
			);

		var destination = await store.FirstOrDefaultAsync<InventoryAggregate>(
			i => i.ProductId == source.ProductId && i.LocationId == destinationLocation.LocationId,
			cancellationToken
		);
		if (destination is null)
		{
			destination = await store.CreateAsync<InventoryAggregate>(cancellationToken: cancellationToken);
			destination.Create(
				source.ProductId,
				source.ProductName,
				destinationLocation.LocationId,
				destinationLocation.LocationName,
				initialQuantity: 0
			);
		}

		source.AdjustStock(
			source.QuantityOnHand - quantity,
			$"Transfer to {destinationLocation.LocationName}: {reason}"
		);

		destination.ReceiveStock(quantity);

		await using var transaction = transactionFactory.Create();
		transaction.Enlist(source, store);
		transaction.Enlist(destination, store);

		/*
		 * The above can be replaced with the following, but as it's an extension method
		 * it's not testable in the same way:
		 *
		 * await using var transaction = store.Enlist(source, destination);
		 */

		var transactionResult = await transaction.CommitAsync(cancellationToken);

		if (transactionResult.Success)
			return StockTransferResult.Success(source, destination, quantity);

		// A lost concurrency check is reported, not thrown, so the caller can tell a retryable race
		// apart from a genuine failure.
		var conflicted = transactionResult.Results.Any(result => result.Failure is ConcurrencyConflict);

		return StockTransferResult.Fail(
			conflicted
				? "Another user modified the stock concurrently. Nothing was saved - please refresh and try again."
				: "Failed to transfer stock. Nothing was saved."
		);
	}
}
