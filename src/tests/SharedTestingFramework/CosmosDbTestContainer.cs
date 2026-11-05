using System.Globalization;
using Purview.Containers;

namespace Purview.EventSourcing;

/// <summary>
/// The Azure Cosmos DB emulator. Purview.Containers has no Cosmos DB module, so the endpoint, the
/// connection string and the client used to reach the emulator are assembled here.
/// </summary>
public sealed class CosmosDbTestContainer(IContainer container) : IAsyncDisposable
{
	public const ushort CosmosDbPort = 8081;

	/// <summary>
	/// The published emulator account key. It is a fixed, documented constant rather than a credential.
	/// </summary>
	public const string WellKnownAccountKey =
		"C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

	public IContainer Container { get; } = container;

	public Task StartAsync(CancellationToken cancellationToken = default) => Container.StartAsync(cancellationToken);

	/// <summary>
	/// The vnext emulator serves the gateway over plain HTTP, and the host binding is IPv4 loopback
	/// only, so the endpoint is addressed as <c>127.0.0.1</c> rather than <c>localhost</c>.
	/// </summary>
	public Uri GetEndpoint() =>
		new(
			string.Create(
				CultureInfo.InvariantCulture,
				$"http://127.0.0.1:{Container.GetMappedPublicPort(CosmosDbPort)}/"
			)
		);

	public string GetConnectionString() =>
		string.Create(
			CultureInfo.InvariantCulture,
			$"AccountEndpoint={GetEndpoint()};AccountKey={WellKnownAccountKey};"
		);

	/// <summary>
	/// Creates a client that accepts the emulator's certificate, for the Cosmos SDK and Entity
	/// Framework to reach it.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability",
		"CA2000:Dispose objects before losing scope",
		Justification = "The handler is owned by the returned HttpClient."
	)]
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Security",
		"CA5400:HttpClient may be created without enabling CheckCertificateRevocationList",
		Justification = "The emulator presents a self-signed certificate; this client only ever talks to it."
	)]
	public static HttpClient CreateHttpClient() =>
		new(
			new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback =
					HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
			}
		);

	public ValueTask DisposeAsync() => Container.DisposeAsync();
}
