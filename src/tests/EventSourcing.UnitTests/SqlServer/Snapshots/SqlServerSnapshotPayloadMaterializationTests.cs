using System.Data.Common;
using System.Linq.Expressions;
using System.Net.Sockets;
using Purview.EventSourcing.SqlServer.Client;

namespace Purview.EventSourcing.SqlServer.Snapshots;

/// <summary>
/// Guards SQL Server snapshot payload materialization shapes without requiring a server: query compilation
/// (model build plus JSON shaper generation) happens before any connection is opened, so reaching a data-access
/// failure proves that EF compiled the payload materializer.
/// </summary>
public sealed class SqlServerSnapshotPayloadMaterializationTests
{
	[Test]
	public async Task QueryByAggregateTypeAsync_GivenReadonlyStructNestedInReadonlyStruct_CompilesPayloadMaterialization()
	{
		var failure = await CompileSnapshotQueryAsync<ReadonlyStructAggregate>(a =>
			a.Capture.User.DisplayName == "jane"
		);

		await Assert
			.That(ReachedDataAccess(failure))
			.IsTrue()
			.Because($"EF failed to materialize the snapshot payload: {failure}");
	}

	[Test]
	public async Task QueryByAggregateTypeAsync_GivenStructNestedInStruct_CompilesPayloadMaterialization()
	{
		var failure = await CompileSnapshotQueryAsync<StructAggregate>(a => a.Capture.User.DisplayName == "jane");

		await Assert
			.That(ReachedDataAccess(failure))
			.IsTrue()
			.Because($"EF failed to materialize the snapshot payload: {failure}");
	}

	// A null result means the query executed; a network/database failure means EF compiled the payload
	// materializer and only then attempted to reach the store.
	static bool ReachedDataAccess(Exception? failure)
	{
		for (var current = failure; current is not null; current = current.InnerException)
		{
			if (current is DbException or SocketException or TimeoutException)
				return true;
		}

		return failure is null;
	}

	static async Task<Exception?> CompileSnapshotQueryAsync<T>(Expression<Func<T, bool>> whereClause)
		where T : class
	{
		SqlServerClient client = new(
			new SqlServerClientOptions(
				"Server=localhost;Database=SnapshotMaterialization;Trusted_Connection=True;TrustServerCertificate=True;",
				UseDataCompression: false
			)
			{
				AutoCreateTable = false,
			}
		);

		try
		{
			await client.QueryByAggregateTypeAsync(typeof(T).Name, whereClause, null, 0, 10);
			return null;
		}
		catch (Exception exception)
		{
			// No SQL Server instance is reachable; reaching the data-access path means EF compiled the query.
			return exception;
		}
	}

	sealed class ReadonlyStructAggregate
	{
		public string Id { get; set; } = Guid.NewGuid().ToString("D");

		public ReadonlyCapture Capture { get; set; } = new(ReadonlyUserInfo.Empty, DateTimeOffset.UtcNow);
	}

	sealed class StructAggregate
	{
		public string Id { get; set; } = Guid.NewGuid().ToString("D");

		public Capture Capture { get; set; } = new();
	}

	// Mirrors the generated value-object shape: members are init-only and the generator emits a public
	// parameterless constructor because EF cannot bind complex constructor parameters in JSON payloads.
	readonly record struct ReadonlyCapture(ReadonlyUserInfo User, DateTimeOffset OccurredAt)
	{
		public ReadonlyCapture()
			: this(ReadonlyUserInfo.Empty, default) { }
	}

	readonly record struct ReadonlyUserInfo(Guid Id, string? DisplayName)
	{
		public static readonly ReadonlyUserInfo Empty = new(Guid.Empty, null);
	}

	struct Capture
	{
		public Capture() { }

		public UserInfo User { get; set; } = UserInfo.Empty;

		public DateTimeOffset OccurredAt { get; set; }
	}

	readonly record struct UserInfo(Guid Id, string? DisplayName)
	{
		public static readonly UserInfo Empty = new(Guid.Empty, null);
	}
}
