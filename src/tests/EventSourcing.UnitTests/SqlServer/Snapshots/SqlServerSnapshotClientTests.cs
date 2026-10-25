using System.Reflection;
using Purview.EventSourcing.SqlServer.Client;

namespace Purview.EventSourcing.SqlServer.Snapshots;

public sealed class SqlServerSnapshotClientTests
{
	[Test]
	public async Task Constructor_GivenDefaultOptions_DoesNotUseLegacyEnsureTableSql()
	{
		// Arrange & Act
		SqlServerClient client = new(
			new SqlServerClientOptions("Server=.;Database=Test;Trusted_Connection=True;", false)
			{
				SchemaName = "dbo",
				TableName = "Snapshots",
				AutoCreateTable = false,
			}
		);

		var ensureTableSqlField = typeof(SqlServerClient).GetField(
			"_ensureTableSql",
			BindingFlags.Instance | BindingFlags.NonPublic
		);

		// Assert
		await Assert.That(client).IsNotNull();
		await Assert.That(ensureTableSqlField).IsNull();
	}
}
