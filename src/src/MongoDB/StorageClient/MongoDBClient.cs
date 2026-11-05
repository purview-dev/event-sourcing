using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.MongoDB.StorageClient;

sealed partial class MongoDBClient : IDisposable
{
	readonly IMongoDBClientTelemetry _telemetry;

	readonly MongoDBConfiguration _configuration;
	readonly MongoClient _client;
	readonly IMongoDatabase _database;

	readonly string _collectionName;

	readonly ICurrentTenant _currentTenant;

	/// <summary>
	/// Narrows a filter to the current tenant.
	/// </summary>
	/// <remarks>
	/// Applied by every read and delete rather than by each caller: a query that forgets the tenant is the
	/// failure this exists to prevent. A document with no recorded owner stays visible, so data written
	/// before tenancy was configured is still readable.
	/// </remarks>
	/// <summary>
	/// Records the current tenant on a document before it is written.
	/// </summary>
	/// <remarks>
	/// Applied centrally so a new write path cannot omit it; an unstamped document would then be hidden
	/// from its own tenant by <see cref="Scoped{T}"/>.
	/// </remarks>
	T Stamp<T>(T document)
	{
		if (document is IEntity entity)
			entity.TenantId = _currentTenant.Current.TenantId;

		return document;
	}

	FilterDefinition<T> Scoped<T>(FilterDefinition<T> predicate)
	{
		var scope = _currentTenant.Current;
		if (scope.IsSystem)
			return predicate;

		FilterDefinitionBuilder<T> builder = new();

		// An unowned document is visible only to the system scope: treating it as visible to every
		// tenant would turn a missed stamp on any write path into silent cross-tenant exposure.
		return builder.And(predicate, builder.Eq(nameof(IEntity.TenantId), scope.TenantId));
	}

	static readonly Lazy<bool> Initialized = new(Init);

#if EventsBasedStore
	readonly static Lazy<bool> EventsInitialized = new(EventsInit);
#endif

	static bool Init()
	{
		try
		{
			BsonSerializer.RegisterSerializationProvider(new MongoDBAggregateSerializationProvider());

			var iEntityType = typeof(IEntity);

			BsonSerializer.RegisterSerializer(new ObjectSerializer(iEntityType.IsAssignableFrom));
		}
#pragma warning disable CA1031
		catch
#pragma warning restore CA1031
		{
			return false;
		}

		return true;
	}

#if EventsBasedStore

	static bool EventsInit()
	{
		try
		{
			Type[] entityType =
			[
				typeof(EventEntity),
				typeof(IdempotencyMarkerEntity),
				typeof(SnapshotEntity),
				typeof(StreamVersionEntity),
			];

			BsonSerializer.RegisterSerializer(new ObjectSerializer(t => Array.IndexOf(entityType, t) > -1));
		}
#pragma warning disable CA1031
		catch
#pragma warning restore CA1031
		{
			return false;
		}

		return true;
	}
#endif

	public MongoDBClient(
		IMongoDBClientTelemetry telemetry,
		MongoDBConfiguration configuration,
		string? databaseOverride = null,
		string? collectionOverride = null,
		ICurrentTenant? currentTenant = null
	)
	{
		_telemetry = telemetry;
		_configuration = configuration;
		_currentTenant = currentTenant ?? new SystemCurrentTenant();

		var settings = MongoClientSettings.FromConnectionString(configuration.ConnectionString);
		settings.ApplicationName = configuration.ApplicationName;

		_client = new MongoClient(settings);
		_database = _client.GetDatabase(databaseOverride ?? configuration.Database);
		_collectionName = collectionOverride ?? configuration.Collection;

		if (Initialized.Value)
			_telemetry.Initialized();

#if EventsBasedStore

		if (EventsInitialized.Value)
			_telemetry.EventsInitialized();
#endif
	}

	static FilterDefinition<T> BuildPredicate<T>(string id, int? entityType)
	{
		FilterDefinitionBuilder<T> builder = new();

		return entityType == null
			? builder.Eq("_id", id)
			: builder.And(builder.Eq("_id", id), builder.Eq(nameof(IEntity.EntityType), entityType));
	}

	sealed class StringObjectIdIdGeneratorConventionThatWorks : ConventionBase, IPostProcessingConvention
	{
		public void PostProcess(BsonClassMap classMap)
		{
			var idMemberMap = classMap.IdMemberMap;
			if (idMemberMap == null || idMemberMap.IdGenerator != null)
				return;

			if (idMemberMap.MemberType == typeof(string))
			{
				idMemberMap
					.SetIdGenerator(StringObjectIdGenerator.Instance)
					.SetSerializer(new StringSerializer(BsonType.ObjectId));
			}
		}
	}

	public void Dispose()
	{
		GC.SuppressFinalize(this);

		_client?.Dispose();
	}
}
