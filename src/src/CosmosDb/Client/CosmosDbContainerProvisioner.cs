using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;
using Purview.EventSourcing.CosmosDb.Snapshots;

namespace Purview.EventSourcing.CosmosDb.Client;

/// <summary>
/// Creates and reconciles the Cosmos DB database and container.
/// </summary>
/// <remarks>
/// Entity Framework does not emit a container indexing policy, and its <c>EnsureCreated</c> is documented
/// as a deployment-time operation that cannot run against a management plane secured with RBAC, so
/// provisioning stays on the Azure Cosmos DB SDK while all data access goes through Entity Framework.
/// </remarks>
sealed class CosmosDbContainerProvisioner : IAsyncDisposable
{
	static readonly ConcurrentDictionary<string, CosmosClient> CosmosDbClients = new();
	static readonly ConcurrentDictionary<string, AsyncLazy<Database>> CreatedDatabases = new();
	static readonly ConcurrentDictionary<string, AsyncLazy<Container>> CreatedContainers = new();

	readonly CosmosDbEventStoreOptions _cosmosDbOptions;
	readonly CosmosDbClientOverrides? _overrides;

	readonly string _containerName;
	readonly string _partitionKeyPath;
	readonly string _databaseCreatedKey;
	readonly string _containerCreatedKey;

	readonly AsyncLazy<Container> _container;

	public CosmosDbContainerProvisioner(
		[NotNull] CosmosDbEventStoreOptions cosmosDbOptions,
		string? partitionKeyOverride = null,
		string? containerNameOverride = null,
		CosmosDbClientOverrides? overrides = null
	)
	{
		_cosmosDbOptions = cosmosDbOptions;
		_overrides = overrides;

		_containerName = containerNameOverride ?? cosmosDbOptions.Container;
		_partitionKeyPath = partitionKeyOverride ?? cosmosDbOptions.PartitionKeyPath;

		_databaseCreatedKey = $"{cosmosDbOptions.ConnectionString}-{cosmosDbOptions.Database}";
		_containerCreatedKey = $"{cosmosDbOptions.ConnectionString}-{_containerName}";

		_container = new AsyncLazy<Container>(InitializeAsync);
	}

	public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default) =>
		await _container.GetValueAsync(cancellationToken);

	public async Task DeleteContainerAsync(CancellationToken cancellationToken = default)
	{
		var container = await _container.GetValueAsync(cancellationToken);
		await container.DeleteContainerAsync(cancellationToken: cancellationToken);

		_ = CreatedContainers.TryRemove(_containerCreatedKey, out _);
	}

	async Task<Container> InitializeAsync(CancellationToken cancellationToken)
	{
		var client = GetOrCreateClient();
		await InitializeDatabaseAsync(client, cancellationToken);

		return await InitializeContainerAsync(cancellationToken);
	}

	async Task<Container> InitializeContainerAsync(CancellationToken cancellationToken)
	{
		return await CreatedContainers
			.GetOrAdd(
				_containerCreatedKey,
				_ => new AsyncLazy<Container>(async ct =>
				{
					var response =
						await GetOrCreateContainerAsync(ct)
						?? throw new NullReferenceException(
							$"Unable to get the container response for '{_containerName}'"
						);
					return response.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.Accepted
						? response.Container
						: throw new InvalidOperationException(
							$"Unable to get or create the container '{_containerName}', with status code: {response.StatusCode}"
						);
				})
			)
			.GetValueAsync(cancellationToken);
	}

	async Task<Database> InitializeDatabaseAsync(CosmosClient client, CancellationToken cancellationToken)
	{
		return await CreatedDatabases
			.GetOrAdd(
				_databaseCreatedKey,
				_ => new AsyncLazy<Database>(async ct =>
				{
					var response = await client.CreateDatabaseIfNotExistsAsync(
						_cosmosDbOptions.Database,
						throughput: _cosmosDbOptions.DatabaseThroughput,
						cancellationToken: ct
					);
					return
						response.StatusCode
							is System.Net.HttpStatusCode.OK
								or System.Net.HttpStatusCode.Accepted
								or System.Net.HttpStatusCode.Created
						? response.Database
						: throw new InvalidOperationException(
							$"Unable to get or create the database '{_cosmosDbOptions.Database}', with status code: {response.StatusCode}"
						);
				})
			)
			.GetValueAsync(cancellationToken);
	}

	async Task<ContainerResponse?> GetOrCreateContainerAsync(CancellationToken cancellationToken)
	{
		var client = GetOrCreateClient();
		var database = await InitializeDatabaseAsync(client, cancellationToken);
		var container = database.GetContainer(_containerName);

		var response = await container.ReadContainerStreamAsync(cancellationToken: cancellationToken);
		var containerExists = response.StatusCode == System.Net.HttpStatusCode.OK;

		var indexOptions = _cosmosDbOptions.IndexOptions;
		if (containerExists)
		{
			// Attempt to modify the container...
			var containerRequiresUpdate = false;
			var containerResponse = await container.ReadContainerAsync(cancellationToken: cancellationToken);

			#region Update existing resource

			if (indexOptions.Automatic != containerResponse.Resource.IndexingPolicy.Automatic)
			{
				containerResponse.Resource.IndexingPolicy.Automatic = indexOptions.Automatic;
				containerRequiresUpdate = true;
			}

			if (indexOptions.IndexingModel != containerResponse.Resource.IndexingPolicy.IndexingMode)
			{
				containerResponse.Resource.IndexingPolicy.IndexingMode = indexOptions.IndexingModel;
				containerRequiresUpdate = true;
			}

			foreach (var includePath in indexOptions.IncludedPaths)
			{
				if (!containerResponse.Resource.IndexingPolicy.IncludedPaths.Any(m => m.Path == includePath))
				{
					containerResponse.Resource.IndexingPolicy.IncludedPaths.Add(
						new IncludedPath { Path = includePath }
					);
					containerRequiresUpdate = true;
				}
			}

			foreach (var excludePath in indexOptions.ExcludedPaths)
			{
				if (!containerResponse.Resource.IndexingPolicy.ExcludedPaths.Any(m => m.Path == excludePath))
				{
					containerResponse.Resource.IndexingPolicy.ExcludedPaths.Add(
						new ExcludedPath { Path = excludePath }
					);
					containerRequiresUpdate = true;
				}
			}

			foreach (var spatialIndex in indexOptions.SpatialIndices)
			{
				if (!containerResponse.Resource.IndexingPolicy.SpatialIndexes.Any(m => m.Path == spatialIndex.Path))
				{
					containerResponse.Resource.IndexingPolicy.SpatialIndexes.Add(spatialIndex);
					containerRequiresUpdate = true;
				}
			}

			foreach (var compositeIndexSetFromConfig in indexOptions.CompositeIndices)
			{
				var existingSet = containerResponse.Resource.IndexingPolicy.CompositeIndexes.Any(
					existingCompositeIndexSet =>
						existingCompositeIndexSet.All(existingCompositeIndex =>
							compositeIndexSetFromConfig.Any(compositeIndexFromConfig =>
								compositeIndexFromConfig.Path == existingCompositeIndex.Path
								&& compositeIndexFromConfig.Order == existingCompositeIndex.Order
							)
						)
				);

				if (!existingSet)
				{
					containerResponse.Resource.IndexingPolicy.CompositeIndexes.Add([
						with([.. compositeIndexSetFromConfig]),
					]);
					containerRequiresUpdate = true;
				}
			}

			#endregion Update existing resource

			if (containerRequiresUpdate)
			{
				try
				{
					await container.ReplaceContainerStreamAsync(
						containerResponse.Resource,
						cancellationToken: cancellationToken
					);
					return await container.ReadContainerAsync(cancellationToken: cancellationToken);
				}
#pragma warning disable CA1031
				catch
#pragma warning restore CA1031
				{
					// Hopefully this is just because it was already created, perhaps when running in a multi-instance environment...
					// Should we do something with this?
				}
			}

			return containerResponse;
		}
		else
		{
			// Attempt to create the container...
			var containerBuilder = database.DefineContainer(_containerName, _partitionKeyPath);

			#region Attempt to create the container

			var indexingPolicyBuilder = containerBuilder
				.WithIndexingPolicy()
				.WithAutomaticIndexing(indexOptions.Automatic)
				.WithIndexingMode(indexOptions.IndexingModel);

			var includedPathBuilder = indexingPolicyBuilder.WithIncludedPaths();
			foreach (var includedPath in indexOptions.IncludedPaths)
				includedPathBuilder = includedPathBuilder.Path(includedPath);

			indexingPolicyBuilder = includedPathBuilder.Attach();

			var excludedPathBuilder = indexingPolicyBuilder.WithExcludedPaths();
			foreach (var excludedPath in indexOptions.ExcludedPaths)
				excludedPathBuilder = excludedPathBuilder.Path(excludedPath);

			indexingPolicyBuilder = excludedPathBuilder.Attach();

			foreach (var compositeIndexSet in indexOptions.CompositeIndices)
			{
				var compositeIndexBuilder = indexingPolicyBuilder.WithCompositeIndex();
				foreach (var compositeIndex in compositeIndexSet)
					compositeIndexBuilder.Path(compositeIndex.Path, compositeIndex.Order);

				indexingPolicyBuilder = compositeIndexBuilder.Attach();
			}

			containerBuilder = indexingPolicyBuilder.Attach();

			#endregion Attempt to create the container

			var containerResponse = await containerBuilder.CreateIfNotExistsAsync(
				_cosmosDbOptions.ContainerThroughput,
				cancellationToken
			);
			if (containerResponse.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.Accepted)
				return containerResponse;

			// Hoping that someone came in between and created it while we were waiting.
			return await container.ReadContainerAsync(cancellationToken: cancellationToken);
		}
	}

	CosmosClient GetOrCreateClient()
	{
		return CosmosDbClients.GetOrAdd(
			$"{_cosmosDbOptions.ConnectionString}".ToUpperInvariant(),
			_ =>
			{
				CosmosClientOptions clientOptions = new()
				{
					ConnectionMode = _cosmosDbOptions.ConnectionMode,
					RequestTimeout = TimeSpan.FromSeconds(
						_cosmosDbOptions.RequestTimeoutInSeconds ?? CosmosDbOptions.DefaultRequestTimeout
					),
				};

				if (_overrides?.HttpClientFactory is not null)
				{
					clientOptions.HttpClientFactory = _overrides.HttpClientFactory;
					clientOptions.LimitToEndpoint = _overrides.LimitToEndpoint;
				}
				else if (_cosmosDbOptions.IgnoreSSLWarnings)
				{
					clientOptions.HttpClientFactory = () =>
					{
						HttpMessageHandler httpMessageHandler = new HttpClientHandler
						{
							ServerCertificateCustomValidationCallback =
								HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
						};

						return new(httpMessageHandler);
					};
				}

				return new(_cosmosDbOptions.ConnectionString, clientOptions);
			}
		);
	}

	public async ValueTask DisposeAsync()
	{
		if (_container.IsValueCreated)
			await _container.DisposeAsync();
	}
}
