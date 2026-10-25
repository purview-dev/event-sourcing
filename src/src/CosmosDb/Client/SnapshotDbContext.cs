using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Purview.EventSourcing.CosmosDb.Snapshots;
using Purview.EventSourcing.EntityFrameworkCore.Mapping;

namespace Purview.EventSourcing.CosmosDb.Client;

/// <summary>
/// Maps <typeparamref name="TAggregate"/> onto the Cosmos DB container as the document root, so caller
/// predicates translate against the document itself rather than a nested payload.
/// </summary>
/// <typeparam name="TAggregate">The aggregate type.</typeparam>
sealed class SnapshotDbContext<TAggregate> : DbContext, ISnapshotModelCacheKeySource
	where TAggregate : class
{
	/// <summary>
	/// The shadow property carrying the document id. Aggregates hold their identity on
	/// <c>Details.Id</c>, and a key cannot live inside a complex type.
	/// </summary>
	public const string IdPropertyName = "id";

	/// <summary>
	/// The shadow property carrying the partition key. The CLR <c>AggregateType</c> member is read-only
	/// and is rebuilt by the aggregate's own constructor, so the value is persisted separately.
	/// </summary>
	public const string PartitionKeyPropertyName = "__aggregateType";

	const string PartitionKeyJsonPropertyName = "AggregateType";

	/// <summary>
	/// The container partition key path this model requires.
	/// </summary>
	public const string PartitionKeyPath = "/" + PartitionKeyJsonPropertyName;

	/// <summary>
	/// The shadow property recording the tenant that owns the snapshot.
	/// </summary>
	public const string TenantPropertyName = "__tenantId";

	readonly string _databaseName;
	readonly string _containerName;

	public SnapshotDbContext(
		DbContextOptions<SnapshotDbContext<TAggregate>> contextOptions,
		string databaseName,
		string containerName
	)
		: base(contextOptions)
	{
		_databaseName = databaseName;
		_containerName = containerName;
	}

	public DbSet<TAggregate> Snapshots => Set<TAggregate>();

	public object CreateModelCacheKey(bool designTime) =>
		(typeof(SnapshotDbContext<>), typeof(TAggregate), _databaseName, _containerName, designTime);

	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		SnapshotModelMapper.RegisterScalarValueObjectConversions(configurationBuilder, typeof(TAggregate));
	}

	/// <summary>
	/// Builds the context options for a snapshot container.
	/// </summary>
	/// <remarks>
	/// Entity Framework caches its internal service provider per distinct set of options, so these are
	/// built once per container and reused: a fresh set per context would build a new provider every
	/// time, which EF reports as <c>ManyServiceProvidersCreatedWarning</c> once it has seen twenty.
	/// </remarks>
	public static DbContextOptions<SnapshotDbContext<TAggregate>> BuildContextOptions(
		CosmosDbEventStoreOptions options,
		CosmosDbClientOverrides? overrides
	)
	{
		ArgumentNullException.ThrowIfNull(options);

		DbContextOptionsBuilder<SnapshotDbContext<TAggregate>> optionsBuilder = new();

		optionsBuilder.UseCosmos(
			options.ConnectionString,
			options.Database,
			cosmos =>
			{
				cosmos.ConnectionMode(options.ConnectionMode);
				cosmos.RequestTimeout(
					TimeSpan.FromSeconds(options.RequestTimeoutInSeconds ?? CosmosDbOptions.DefaultRequestTimeout)
				);

				if (overrides?.HttpClientFactory is not null)
				{
					cosmos.HttpClientFactory(overrides.HttpClientFactory);
					if (overrides.LimitToEndpoint)
						cosmos.LimitToEndpoint();
				}
				else if (options.IgnoreSSLWarnings)
				{
					cosmos.HttpClientFactory(CreateInsecureHttpClient);
				}
			}
		);

		optionsBuilder.ReplaceService<IModelCacheKeyFactory, SnapshotModelCacheKeyFactory>();

		return optionsBuilder.Options;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability",
		"CA2000:Dispose objects before losing scope",
		Justification = "The handler is owned by the returned HttpClient."
	)]
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Security",
		"CA5400:HttpClient may be created without enabling CheckCertificateRevocationList",
		Justification = "Opted into explicitly through IgnoreSSLWarnings, which documents itself as emulator-only."
	)]
	static HttpClient CreateInsecureHttpClient() =>
		new(
			new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback =
					HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
			}
		);

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// Snapshot payloads are value-object graphs whose members are init-only (or value-object
		// struct members). PreferProperty writes members through their accessors (init included) and
		// only falls back to the backing field when no setter exists.
		modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferProperty);

		var entity = modelBuilder.Entity<TAggregate>();
		entity.ToContainer(_containerName);

		// One aggregate type per partition, so documents are already unambiguous without EF's $type
		// discriminator - which would otherwise be added to every document.
		entity.HasNoDiscriminator();

		entity.Property<string>(IdPropertyName).ToJsonProperty(IdPropertyName);
		entity.Property<string>(PartitionKeyPropertyName).ToJsonProperty(PartitionKeyJsonPropertyName);
		entity.Property<string>(TenantPropertyName).ToJsonProperty(TenantPropertyName);
		entity.HasKey(IdPropertyName);
		entity.HasPartitionKey(PartitionKeyPropertyName);

		SnapshotModelMapper.ValidateAggregatePayloadShape(typeof(TAggregate));
		SnapshotModelMapper.ConfigureGraph(SnapshotGraphBuilder.For(entity), typeof(TAggregate));
	}
}
