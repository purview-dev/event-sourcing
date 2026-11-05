using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.SqlServer.Events;

namespace Purview.EventSourcing.Admin.SqlServer;

/// <summary>
/// Reports whether the configured SQL Server event store is reachable.
/// </summary>
/// <param name="options">The event-store options supplying the connection string.</param>
public sealed class SqlServerAdminStorageProbe(IOptions<SqlServerEventStoreOptions> options) : IAdminStorageProbe
{
	/// <inheritdoc/>
	public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
	{
		// Opening the connection is the probe: it exercises resolution, network, and authentication
		// without reading a row, so a frequently polled health endpoint stays cheap.
		await using SqlConnection connection = new(options.Value.ConnectionString);
		await connection.OpenAsync(cancellationToken);

		return connection.State is System.Data.ConnectionState.Open;
	}
}
