namespace Purview.EventSourcing.AzureStorage.Entities;

/// <summary>
/// The payload stored on an <see cref="IdempotencyMarkerEntity"/>.
/// </summary>
/// <remarks>
/// Records the aggregate event versions that were persisted as part of the idempotency marker.
/// </remarks>
public sealed class IdempotencyMarkerEventPayload
{
	/// <summary>
	/// Gets or sets the aggregate event versions persisted for the idempotency id.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Performance",
		"CA1819:Properties should not return arrays",
		Justification = "This is a DTO."
	)]
	public long[] EventIds { get; set; } = default!;
}
