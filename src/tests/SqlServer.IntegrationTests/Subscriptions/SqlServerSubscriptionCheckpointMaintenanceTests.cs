using Microsoft.Extensions.Options;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.SqlServer.Events;
using Purview.EventSourcing.Subscriptions;

namespace Purview.EventSourcing.SqlServer.Subscriptions;

/// <summary>
/// The shared checkpoint-maintenance contract, against real SQL Server.
/// </summary>
/// <remarks>
/// One table for the whole class: the contract distinguishes rows by key alone, which is the only
/// way a delete that catches one row too many shows up.
/// </remarks>
[ClassDataSource<SqlServerEventStoreFixture>(Shared = SharedType.PerTestSession)]
[InheritsTests]
public sealed class SqlServerSubscriptionCheckpointMaintenanceTests(SqlServerEventStoreFixture fixture)
	: SubscriptionCheckpointMaintenanceContractTestsBase
{
	readonly string _tableName = $"Checkpoints_{Guid.NewGuid():N}";

	protected override ISubscriptionCheckpointStore CreateCheckpointStore() =>
		new SqlServerSubscriptionCheckpointStore(
			Options.Create(
				new SqlServerSubscriptionCheckpointStoreOptions
				{
					ConnectionString = fixture.ConnectionString,
					TableName = _tableName,
				}
			),
			Options.Create(new SqlServerEventStoreOptions { ConnectionString = fixture.ConnectionString })
		);
}
