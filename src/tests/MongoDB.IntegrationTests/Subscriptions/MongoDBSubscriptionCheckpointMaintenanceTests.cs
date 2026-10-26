using Microsoft.Extensions.Options;
using Purview.EventSourcing.Fixtures.MongoDB;
using Purview.EventSourcing.MongoDB.Events;
using Purview.EventSourcing.Subscriptions;

namespace Purview.EventSourcing.MongoDB.Subscriptions;

/// <summary>
/// The shared checkpoint-maintenance contract, against real MongoDB.
/// </summary>
/// <remarks>
/// One database and collection for the whole class: the contract distinguishes documents by key
/// alone, and Mongo's key is a compound <c>_id</c> - a delete matched against the wrong part of it
/// is exactly what this is here to catch.
/// </remarks>
[ClassDataSource<MongoDBEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class MongoDBSubscriptionCheckpointMaintenanceTests(MongoDBEventStoreFixture fixture)
	: SubscriptionCheckpointMaintenanceContractTestsBase
{
	readonly string _database = $"chk{Guid.NewGuid():N}";

	protected override ISubscriptionCheckpointStore CreateCheckpointStore() =>
		new MongoDBSubscriptionCheckpointStore(
			Options.Create(
				new MongoDBSubscriptionCheckpointStoreOptions
				{
					ConnectionString = fixture.ConnectionString,
					Database = _database,
					CollectionName = "Checkpoints",
				}
			),
			Options.Create(
				new MongoDBEventStoreOptions { ConnectionString = fixture.ConnectionString, Database = "unused" }
			)
		);
}
