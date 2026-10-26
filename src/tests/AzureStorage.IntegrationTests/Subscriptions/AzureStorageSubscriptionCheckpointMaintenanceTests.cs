using Microsoft.Extensions.Options;
using Purview.EventSourcing.Fixtures.AzureStorage;
using Purview.EventSourcing.Subscriptions;

namespace Purview.EventSourcing.AzureStorage.Subscriptions;

/// <summary>
/// The shared checkpoint-maintenance contract, against real Azure Table Storage.
/// </summary>
/// <remarks>
/// One table for the whole class. Azure composes the subscription and tenant into the partition key,
/// so retiring a subscription is a whole-partition delete - which makes the "leaves another tenant
/// alone" case a test of how that key is built, not of a predicate.
/// </remarks>
[ClassDataSource<TableEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class AzureStorageSubscriptionCheckpointMaintenanceTests(TableEventStoreFixture fixture)
	: SubscriptionCheckpointMaintenanceContractTestsBase
{
	// Azure table names are alphanumeric and must start with a letter.
	readonly string _tableName = $"Checkpoints{Guid.NewGuid():N}";

	protected override ISubscriptionCheckpointStore CreateCheckpointStore() =>
		new AzureStorageSubscriptionCheckpointStore(
			Options.Create(
				new AzureStorageSubscriptionCheckpointStoreOptions
				{
					ConnectionString = fixture.ConnectionString,
					TableName = _tableName,
				}
			),
			Options.Create(new AzureStorageEventStoreOptions { ConnectionString = fixture.ConnectionString })
		);
}
