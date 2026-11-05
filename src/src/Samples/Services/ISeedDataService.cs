namespace Purview.EventSourcing.Samples.Services;

public interface ISeedDataService
{
	/// <summary>
	/// Seeds the demo store, returning <see langword="false"/> when a save lost a concurrency check -
	/// typically another instance seeding the same store - so the caller can retry.
	/// </summary>
	Task<bool> SeedAsync(CancellationToken cancellationToken = default);
}
