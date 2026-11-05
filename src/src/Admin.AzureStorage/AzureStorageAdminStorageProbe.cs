using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.AzureStorage.Internal;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.AzureStorage;

namespace Purview.EventSourcing.Admin.AzureStorage;

/// <summary>
/// Reports whether the configured Azure Table Storage event store is reachable.
/// </summary>
/// <param name="options">The event-store options supplying the storage connection.</param>
public sealed class AzureStorageAdminStorageProbe(IOptions<AzureStorageEventStoreOptions> options) : IAdminStorageProbe
{
	/// <inheritdoc/>
	public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
	{
		// Listing a single table name is the cheapest call that still proves the account is reachable
		// and the credential is accepted; an empty account is still a reachable account.
		var tableService = AzureStorageAdminTableHelpers.CreateTableServiceClient(options.Value);

		await foreach (var _ in tableService.QueryAsync(maxPerPage: 1, cancellationToken: cancellationToken))
			break;

		return true;
	}
}
