namespace Purview.EventSourcing.SourceGenerator.Common;

/// <summary>
/// Declares the eventsourcing-specific type identities for the generated <see cref="TypeLibrary"/>.
/// The generator mirrors the framework <c>PurviewTypeLibrary</c> shape, so common system types are
/// inherited and only the eventsourcing types and framework-absent system types are declared here.
/// </summary>
[GenerateTypeLibrary(ClassName = "TypeLibrary", Namespace = "Purview.EventSourcing.SourceGenerator.Common")]
static partial class TypeLibrarySpec
{
	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity AggregateAttribute = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity AggregateDefaultsAttribute = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity CollectionEventAttribute = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity CollectionEventOperation = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity ComputedAttribute = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity EventAttribute = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity MetadataAttribute = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity PropertyAttribute = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity SentinelEventAttribute = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity AggregateBase = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity AggregateDetails = default;

	[TypeRef("Purview.EventSourcing.Aggregates")]
	static readonly TypeIdentity IAggregate = default;

	[TypeRef("Purview.EventSourcing.Aggregates.Events")]
	static readonly TypeIdentity EventContractAttribute = default;

	[TypeRef("Purview.EventSourcing.Aggregates.Events")]
	static readonly TypeIdentity EventMetadata = default;

	[TypeRef("Purview.EventSourcing", arity: 1)]
	static readonly TypeIdentity EventStoreList = default;

	[TypeRef("Purview.EventSourcing", arity: 1)]
	static readonly TypeIdentity EventStoreSet = default;

	[TypeRef("Purview.EventSourcing.Serialization")]
	static readonly TypeIdentity ScalarAttribute = default;

	[TypeRef("Purview.EventSourcing.Serialization")]
	static readonly TypeIdentity ValueObjectAttribute = default;

	[TypeRef("Purview.EventSourcing.Serialization")]
	static readonly TypeIdentity ValueObjectDefaultsAttribute = default;

	[TypeRef("Purview.EventSourcing.Serialization")]
	static readonly TypeIdentity ValueObjectDeserializationMode = default;

	[TypeRef(typeof(AttributeUsageAttribute))]
	static readonly TypeIdentity AttributeUsageAttribute = default;

	[TypeRef(typeof(AttributeTargets))]
	static readonly TypeIdentity AttributeTargets = default;

	[TypeRef("System")]
	static readonly TypeIdentity Guid = default;

	[TypeRef("System")]
	static readonly TypeIdentity Uri = default;

	[TypeRef("System")]
	static readonly TypeIdentity HashCode = default;

	[TypeRef("System.Text.Json")]
	static readonly TypeIdentity JsonSerializer = default;

	[TypeRef("System.Text.Json")]
	static readonly TypeIdentity JsonException = default;

	[TypeRef("System.Text.Json")]
	static readonly TypeIdentity JsonSerializerOptions = default;

	[TypeRef("System.Text.Json")]
	static readonly TypeIdentity Utf8JsonReader = default;

	[TypeRef("System.Text.Json")]
	static readonly TypeIdentity Utf8JsonWriter = default;

	[TypeRef("System.Text.Json.Serialization")]
	static readonly TypeIdentity JsonConverter = default;

	[TypeRef("System.Text.Json.Serialization")]
	static readonly TypeIdentity JsonConverterAttribute = default;

	[TypeRef("System.Text.Json.Serialization")]
	static readonly TypeIdentity JsonIgnoreAttribute = default;
}
