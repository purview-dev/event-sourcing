namespace Purview.EventSourcing.SourceGenerator.Common;

public static partial class TypeLibrary
{
	public const string AggregateNamespace = "Purview.EventSourcing.Aggregates";

	public const string EventsNamespace = "Purview.EventSourcing.Aggregates.Events";

	public const string CollectionsNamespace = "Purview.EventSourcing";

	public const string AggregateGeneratorName = "Purview.EventSourcing.AggregateSourceGenerator";

	public const string AggregateAttributeFullTypeName = AggregateNamespace + ".AggregateAttribute";

	public const string AggregateDefaultsAttributeFullTypeName = AggregateNamespace + ".AggregateDefaultsAttribute";

	public const string CollectionEventAttributeFullTypeName = AggregateNamespace + ".CollectionEventAttribute";

	public const string EventAttributeFullTypeName = AggregateNamespace + ".EventAttribute";

	public const string MetadataAttributeFullTypeName = AggregateNamespace + ".MetadataAttribute";

	public const string PropertyAttributeFullTypeName = AggregateNamespace + ".PropertyAttribute";
}
