using System.Globalization;
using Purview.Containers;

namespace Purview.EventSourcing;

/// <summary>
/// A MongoDB container running as a single-node replica set, which the event store's transactions
/// require. Purview.Containers has no MongoDB module, so the replica set is initiated here.
/// </summary>
public sealed class MongoDBTestContainer(IContainer container) : IAsyncDisposable
{
	public const ushort MongoDBPort = 27017;

	static readonly TimeSpan PrimaryTimeout = TimeSpan.FromMinutes(2);

	public IContainer Container { get; } = container;

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		await Container.StartAsync(cancellationToken);
		await InitiateReplicaSetAsync(cancellationToken);
		await WaitForPrimaryAsync(cancellationToken);
	}

	/// <summary>
	/// The replica set advertises its member as <c>127.0.0.1:27017</c>, which is the container's own
	/// address rather than the mapped host port, so clients connect directly instead of discovering
	/// the set.
	/// </summary>
	public string GetConnectionString() =>
		string.Create(
			CultureInfo.InvariantCulture,
			$"mongodb://127.0.0.1:{Container.GetMappedPublicPort(MongoDBPort)}/?directConnection=true"
		);

	public ValueTask DisposeAsync() => Container.DisposeAsync();

	async Task InitiateReplicaSetAsync(CancellationToken cancellationToken)
	{
		var result = await Container.ExecAsync(
			[
				"mongosh",
				"--quiet",
				"--eval",
				$"try {{ rs.status().ok }} catch (e) {{ rs.initiate({{ _id: '{ContainerHelper.MongoDBReplicaSetName}', members: [{{ _id: 0, host: '127.0.0.1:{MongoDBPort}' }}] }}) }}",
			],
			cancellationToken: cancellationToken
		);

		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"Unable to initiate the MongoDB replica set (exit code {result.ExitCode}): {result.Stderr}"
			);
		}
	}

	async Task WaitForPrimaryAsync(CancellationToken cancellationToken)
	{
		var deadline = DateTimeOffset.UtcNow.Add(PrimaryTimeout);
		while (true)
		{
			var result = await Container.ExecAsync(
				["mongosh", "--quiet", "--eval", "db.hello().isWritablePrimary"],
				cancellationToken: cancellationToken
			);

			if (result.ExitCode == 0 && result.Stdout.Contains("true", StringComparison.OrdinalIgnoreCase))
				return;

			if (DateTimeOffset.UtcNow > deadline)
				throw new InvalidOperationException("The MongoDB replica set did not elect a primary in time.");

			await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
		}
	}
}
