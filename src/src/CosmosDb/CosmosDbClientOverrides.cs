namespace Purview.EventSourcing.CosmosDb;

/// <summary>
/// Overrides applied to the underlying Azure Cosmos DB client, for environments - such as the local
/// emulator - that cannot be reached with the default client configuration.
/// </summary>
/// <remarks>
/// These are applied both to the Entity Framework context used for data access and to the client used to
/// create the database and container, so the two always talk to the same endpoint.
/// </remarks>
public sealed class CosmosDbClientOverrides
{
	/// <summary>
	/// Gets the factory used to create the <see cref="HttpClient"/> the Cosmos DB client uses.
	/// </summary>
	public Func<HttpClient>? HttpClientFactory { get; init; }

	/// <summary>
	/// Gets a value indicating whether the client is limited to the configured endpoint, rather than
	/// discovering the account's regional endpoints.
	/// </summary>
	public bool LimitToEndpoint { get; init; }
}
