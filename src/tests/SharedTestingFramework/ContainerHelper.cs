using Purview.Containers;
using Purview.Containers.Azurite;
using Purview.Containers.MsSql;
using Purview.Containers.PostgreSql;
using Purview.Containers.Waiting;

namespace Purview.EventSourcing;

/// <summary>
/// Creates the throwaway containers the integration suites run against, through Purview.Containers -
/// which selects WSL Containers on a Windows host and Docker everywhere else.
/// </summary>
public static partial class ContainerHelper
{
	public static AzuriteContainer CreateAzurite(Action<AzuriteBuilder>? config = null)
	{
		AzuriteBuilder builder = new($"mcr.microsoft.com/azure-storage/azurite:{AzuriteImageTag}");

		// WithCommand replaces the module's command, so the host bindings it sets are repeated here
		// alongside --skipApiVersionCheck: the Azure Storage SDK sends a newer x-ms-version than this
		// Azurite release knows, and rejects every request without it. Each --*Host flag must appear
		// exactly once - a duplicate makes Azurite fall back to binding 127.0.0.1 inside the container,
		// which is not reachable through the mapped port.
		builder.WithCommand(
			"azurite",
			"--blobHost",
			"0.0.0.0",
			"--queueHost",
			"0.0.0.0",
			"--tableHost",
			"0.0.0.0",
			"--skipApiVersionCheck"
		);

		config?.Invoke(builder);

		return builder.Build();
	}

	/// <summary>
	/// Builds the emulator connection string with the real well-known account key.
	/// </summary>
	/// <remarks>
	/// The module deliberately ships a placeholder for <see cref="AzuriteAccount.Key"/>, so the key is
	/// supplied here. It is not a secret: it is the published, fixed development key every Azure Storage
	/// emulator uses.
	/// </remarks>
	public static string GetEmulatorConnectionString(this AzuriteContainer container)
	{
		ArgumentNullException.ThrowIfNull(container);

		return container
			.GetConnectionString()
			.Replace(AzuriteAccount.Key, AzuriteWellKnownAccountKey, StringComparison.Ordinal);
	}

	public static MsSqlContainer CreateMsSql(Action<MsSqlBuilder>? config = null)
	{
		MsSqlBuilder builder = new($"mcr.microsoft.com/mssql/server:{SqlServerImageTag}");
		builder.AcceptLicense();

		config?.Invoke(builder);

		return builder.Build();
	}

	public static PostgreSqlContainer CreatePostgreSql(Action<PostgreSqlBuilder>? config = null)
	{
		PostgreSqlBuilder builder = new($"postgres:{PostgresImageTag}");

		config?.Invoke(builder);

		return builder.Build();
	}

	public static MongoDBTestContainer CreateMongoDB(Action<ContainerBuilder>? config = null)
	{
		ContainerBuilder builder = new();
		builder
			.WithImage($"mongo:{MongoDBImageTag}")
			// The event store uses transactions, which MongoDB only offers on a replica set.
			.WithCommand("--replSet", MongoDBReplicaSetName, "--bind_ip_all")
			.WithPortBinding(MongoDBTestContainer.MongoDBPort, assignRandomHostPort: true)
			// Not a TCP probe: the host port is forwarded - and so accepts connections - before mongod
			// itself is listening, and the replica-set initiation that follows would then be refused.
			// Pinging from inside the container only succeeds once the server answers.
			.WithWaitStrategy(Wait.ForCommand("mongosh", "--quiet", "--eval", "db.adminCommand('ping').ok"));

		config?.Invoke(builder);

		return new(builder.Build());
	}

	public static CosmosDbTestContainer CreateCosmosDB(Action<ContainerBuilder>? config = null)
	{
		ContainerBuilder builder = new();
		builder
			.WithImage($"mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:{CosmosDbImageTag}")
			.WithEnvironment("AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE", "false")
			.WithPortBinding(CosmosDbTestContainer.CosmosDbPort, assignRandomHostPort: true)
			.WithStartupTimeout(TimeSpan.FromMinutes(5))
			// The vnext emulator's HTTP gateway answers before its data engine is up, returning 503
			// ("pgcosmos extension is still starting"); anything else means it is ready.
			.WithWaitStrategy(
				Wait.ForHttp("/")
					.ForPort(CosmosDbTestContainer.CosmosDbPort)
					.ForStatusPredicate(static status => status != (int)System.Net.HttpStatusCode.ServiceUnavailable)
					.AllowInsecureTls()
					.WithTimeout(TimeSpan.FromMinutes(5))
			);

		config?.Invoke(builder);

		return new(builder.Build());
	}
}
