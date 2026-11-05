using TUnit.Core.Interfaces;

namespace Purview.EventSourcing.Fixtures.MongoDB;

public class MongoDBSnapshotEventStoreFixture : IAsyncInitializer, IAsyncDisposable
{
	readonly Containers.Azurite.AzuriteContainer _azuriteContainer;
	readonly MongoDBTestContainer _mongoDBContainer;

	public MongoDBSnapshotEventStoreFixture()
	{
		EventStoreOperationContext.RequiresValidPrincipalIdentifierDefault = false;

		_azuriteContainer = ContainerHelper.CreateAzurite();
		_mongoDBContainer = ContainerHelper.CreateMongoDB();
	}

	public MongoDBSnapshotTestContext CreateContext(int correlationIdsToGenerate = 1, string? collectionName = null) =>
		new(
			_mongoDBContainer.GetConnectionString(),
			_azuriteContainer.GetEmulatorConnectionString(),
			correlationIdsToGenerate,
			collectionName
		);

	public async Task InitializeAsync()
	{
		await _mongoDBContainer.StartAsync();
		await _azuriteContainer.StartAsync();
	}

	public async ValueTask DisposeAsync()
	{
		GC.SuppressFinalize(this);

		await _mongoDBContainer.DisposeAsync();
		await _azuriteContainer.DisposeAsync();
	}
}
