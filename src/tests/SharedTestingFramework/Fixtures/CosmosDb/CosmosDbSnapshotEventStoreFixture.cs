using Purview.EventSourcing.CosmosDb;
using TUnit.Core.Interfaces;

namespace Purview.EventSourcing.Fixtures.CosmosDb;

public class CosmosDbSnapshotEventStoreFixture : IAsyncInitializer, IAsyncDisposable
{
	readonly Containers.Azurite.AzuriteContainer _azuriteContainer;
	readonly CosmosDbTestContainer _cosmosDbContainer;

	// One set of overrides for the whole session: Entity Framework keys its internal service provider
	// on the context options, and these form part of them, so a fresh instance per context would have
	// EF build a provider per test.
	readonly CosmosDbClientOverrides _cosmosDbClientOverrides;

	public CosmosDbSnapshotEventStoreFixture()
	{
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;

		_azuriteContainer = ContainerHelper.CreateAzurite();
		_cosmosDbContainer = ContainerHelper.CreateCosmosDB();

		// The emulator is only reachable through a client that accepts its certificate, and only at the
		// endpoint it advertises. The factory must hand out a new client each time: a Cosmos client
		// configures the one it is given, and rejects one that has already issued a request.
		_cosmosDbClientOverrides = new()
		{
			HttpClientFactory = CosmosDbTestContainer.CreateHttpClient,
			LimitToEndpoint = true,
		};
	}

	public CosmosDbSnapshotEventStoreContext CreateContext(int correlationIdsToGenerate = 1)
	{
		CosmosDbSnapshotEventStoreContext eventStoreContext = new(
			_cosmosDbContainer.GetConnectionString(),
			_cosmosDbClientOverrides,
			_azuriteContainer.GetEmulatorConnectionString()
		);

		eventStoreContext.CreateCosmosDbEventStore(correlationIdsToGenerate: correlationIdsToGenerate);

		return eventStoreContext;
	}

	public async ValueTask DisposeAsync()
	{
		GC.SuppressFinalize(this);

		await _azuriteContainer.DisposeAsync();
		await _cosmosDbContainer.DisposeAsync();
	}

	public async Task InitializeAsync()
	{
		await _azuriteContainer.StartAsync();
		await _cosmosDbContainer.StartAsync();
	}
}
