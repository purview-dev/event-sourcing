namespace Purview.EventSourcing.EntityFrameworkCore.SourceGenerator.Heleprs;

static class TypeLibrary
{
	public const string RootNamespace = "Purview.EventSourcing.EntityFrameworkCore";

	public const string CollectionsNamespace = "Purview.EventSourcing";

	public const string ValueObjectsNamespace = "Purview.ValueObjects.Serialization";

	public static readonly TypeIdentity EFOpaqueAttribute = new(nameof(EFOpaqueAttribute), RootNamespace);

	public static readonly TypeIdentity AggregateAttribute = new(
		nameof(AggregateAttribute),
		"Purview.EventSourcing.Aggregates"
	);

	public static readonly TypeIdentity EventStoreList = new("EventStoreList", CollectionsNamespace, arity: 1);

	public static readonly TypeIdentity EventStoreSet = new("EventStoreSet", CollectionsNamespace, arity: 1);

	public static readonly TypeIdentity ScalarAttribute = new("ScalarAttribute", ValueObjectsNamespace);

	public static readonly TypeIdentity ValueObjectAttribute = new("ValueObjectAttribute", ValueObjectsNamespace);

	public static readonly TypeIdentity JsonIgnoreAttribute = new(
		"JsonIgnoreAttribute",
		"System.Text.Json.Serialization"
	);

	public static readonly TypeIdentity DictionaryKV = new(typeof(Dictionary<,>));

	public static readonly TypeIdentity IDictionaryKV = new(typeof(IDictionary<,>));

	public static readonly TypeIdentity IReadOnlyDictionaryKV = new(typeof(IReadOnlyDictionary<,>));
}
