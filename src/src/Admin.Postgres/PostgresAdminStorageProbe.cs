using Microsoft.Extensions.Options;
using Npgsql;
using Purview.EventSourcing.Admin.Services;
using Purview.EventSourcing.Postgres.Events;

namespace Purview.EventSourcing.Admin.Postgres;

/// <summary>
/// Reports whether the configured PostgreSQL event store is reachable.
/// </summary>
/// <param name="options">The event-store options supplying the connection string.</param>
public sealed class PostgresAdminStorageProbe(IOptions<PostgresEventStoreOptions> options) : IAdminStorageProbe
{
	/// <inheritdoc/>
	public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
	{
		// Opening the connection is the probe: it exercises resolution, network, and authentication
		// without reading a row, so a frequently polled health endpoint stays cheap.
		await using NpgsqlConnection connection = new(options.Value.ConnectionString);
		await connection.OpenAsync(cancellationToken);

		return connection.State is System.Data.ConnectionState.Open;
	}
}
