using Microsoft.Extensions.Options;
using Purview.EventSourcing.Fixtures.Postgres;
using Purview.EventSourcing.Postgres.Events;
using Purview.EventSourcing.Subscriptions;

namespace Purview.EventSourcing.Postgres.Subscriptions;

/// <summary>
/// The shared checkpoint-maintenance contract, against real PostgreSQL.
/// </summary>
/// <remarks>
/// One table for the whole class: the contract distinguishes rows by key alone, which is the only
/// way a delete that catches one row too many shows up.
/// </remarks>
[ClassDataSource<PostgresEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class PostgresSubscriptionCheckpointMaintenanceTests(PostgresEventStoreFixture fixture)
	: SubscriptionCheckpointMaintenanceContractTestsBase
{
	readonly string _tableName = $"Checkpoints_{Guid.NewGuid():N}";

	protected override ISubscriptionCheckpointStore CreateCheckpointStore() =>
		new PostgresSubscriptionCheckpointStore(
			Options.Create(
				new PostgresSubscriptionCheckpointStoreOptions
				{
					ConnectionString = fixture.ConnectionString,
					TableName = _tableName,
				}
			),
			Options.Create(new PostgresEventStoreOptions { ConnectionString = fixture.ConnectionString })
		);
}
