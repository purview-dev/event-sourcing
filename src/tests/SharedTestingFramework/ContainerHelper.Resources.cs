namespace Purview.EventSourcing;

partial class ContainerHelper
{
	public const string AzuriteImageTag = "3.37.0";

	public const string SqlServerImageTag = "2025-latest";

	public const string PostgresImageTag = "18";

	public const string CosmosDbImageTag = "vnext-latest";

	public const string MongoDBImageTag = "8.0";

	public const string MongoDBReplicaSetName = "rs0";

	/// <summary>
	/// The published development account key every Azure Storage emulator accepts. It is a fixed,
	/// documented constant rather than a credential.
	/// </summary>
	public const string AzuriteWellKnownAccountKey =
		"Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
}
