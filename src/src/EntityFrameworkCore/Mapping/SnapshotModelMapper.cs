using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Purview.EventSourcing.EntityFrameworkCore.Conversion;

namespace Purview.EventSourcing.EntityFrameworkCore.Mapping;

/// <summary>
/// Maps an aggregate snapshot graph onto an Entity Framework model. The same walk is used by every
/// EF-backed provider so an aggregate that persists on one provider persists identically on the others.
/// </summary>
static class SnapshotModelMapper
{
	/// <summary>
	/// Registers the conversions for every <c>[Scalar]</c> value object reachable from
	/// <paramref name="rootType"/>, so scalar members are stored as the value they wrap.
	/// </summary>
	/// <param name="configurationBuilder">The model configuration builder.</param>
	/// <param name="rootType">The aggregate type.</param>
	public static void RegisterScalarValueObjectConversions(
		ModelConfigurationBuilder configurationBuilder,
		Type rootType
	)
	{
		ArgumentNullException.ThrowIfNull(configurationBuilder);
		ArgumentNullException.ThrowIfNull(rootType);

		HashSet<Type> visited = [];
		RegisterScalarValueObjectConversionsRecursive(configurationBuilder, rootType, visited);
	}

	/// <summary>
	/// Validates that every member reachable from <paramref name="type"/> can be persisted, throwing a
	/// message that names the offending member rather than failing later inside EF.
	/// </summary>
	/// <param name="type">The aggregate type.</param>
	public static void ValidateAggregatePayloadShape(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);

		HashSet<Type> visited = [];
		ValidateAggregatePayloadShapeRecursive(type, visited);
	}

	/// <summary>
	/// Configures the snapshot graph for <paramref name="type"/> onto <paramref name="builder"/>.
	/// </summary>
	/// <param name="builder">The builder owning the members.</param>
	/// <param name="type">The type being mapped.</param>
	public static void ConfigureGraph(ISnapshotGraphBuilder builder, Type type)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(type);

		HashSet<Type> visited = [];
		ConfigureGraphRecursive(builder, type, visited);
	}

	static void RegisterScalarValueObjectConversionsRecursive(
		ModelConfigurationBuilder configurationBuilder,
		Type type,
		HashSet<Type> visited
	)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (!visited.Add(type))
			return;

		if (SnapshotTypes.IsScalarValueObjectType(type))
		{
			RegisterScalarValueObjectConversion(configurationBuilder, type);
			return;
		}

		if (!SnapshotTypes.ShouldInspectType(type))
			return;

		foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

			if (propertyType == typeof(string) || propertyType == typeof(byte[]))
				continue;

			if (SnapshotTypes.TryGetEnumerableElementType(propertyType, out var elementType))
			{
				RegisterScalarValueObjectConversionsRecursive(configurationBuilder, elementType, visited);
				continue;
			}

			RegisterScalarValueObjectConversionsRecursive(configurationBuilder, propertyType, visited);
		}
	}

	static void RegisterScalarValueObjectConversion(ModelConfigurationBuilder configurationBuilder, Type scalarType)
	{
		var scalarProperty = ScalarValueObjects.GetScalarProperty(scalarType);
		var scalarPropertyType = scalarProperty.PropertyType;
		var converterType = SnapshotTypes.IsPrimitiveLike(scalarPropertyType)
			? typeof(ScalarValueConverter<,>).MakeGenericType(scalarType, scalarPropertyType)
			: typeof(JsonScalarValueConverter<,>).MakeGenericType(scalarType, scalarPropertyType);

		configurationBuilder.Properties(scalarType, builder => builder.HaveConversion(converterType));
	}

	static void ConfigureGraphRecursive(ISnapshotGraphBuilder builder, Type type, HashSet<Type> visited)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (!SnapshotTypes.ShouldInspectType(type) || !visited.Add(type))
			return;

		foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			if (SnapshotTypes.ShouldSkipJsonProperty(type, property))
			{
				builder.Ignore(property.Name);
				continue;
			}

			var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

			if (propertyType == typeof(string) || propertyType == typeof(byte[]))
			{
				MapReadOnlyConstructorBoundProperty(builder, type, property, propertyType);
				continue;
			}

			if (SnapshotTypes.IsScalarValueObjectType(propertyType))
			{
				MapReadOnlyConstructorBoundProperty(builder, type, property, propertyType);
				continue;
			}

			if (SnapshotTypes.HasEfOpaqueAttribute(property))
			{
				builder.PropertyWithConversion(
					propertyType,
					property.Name,
					typeof(OpaqueJsonValueConverter<>).MakeGenericType(propertyType)
				);
				continue;
			}

			if (SnapshotTypes.TryGetEnumerableElementType(propertyType, out var elementType))
			{
				if (SnapshotTypes.IsStructValueObjectType(elementType))
				{
					MapAsJsonProperty(builder, propertyType, property.Name);
					continue;
				}

				if (SnapshotTypes.IsPrimitiveLike(elementType) || SnapshotTypes.IsScalarValueObjectType(elementType))
					builder.PrimitiveCollection(propertyType, property.Name);
				else if (SnapshotTypes.ShouldInspectType(elementType))
					builder.ComplexCollection(
						propertyType,
						property.Name,
						nested => ConfigureGraphRecursive(nested, elementType, [.. visited])
					);
				else
					throw SnapshotTypes.CreateUnsupportedShapeException(type, property);

				continue;
			}

			if (
				SnapshotTypes.IsDictionaryLikeType(propertyType)
				|| SnapshotTypes.IsJsonConvertibleCollectionType(propertyType)
			)
			{
				MapAsJsonProperty(builder, propertyType, property.Name);
				continue;
			}

			if (SnapshotTypes.IsCollectionLikeType(propertyType))
				throw SnapshotTypes.CreateUnsupportedCollectionTypeException(type, property, propertyType);

			if (SnapshotTypes.ShouldOwnType(propertyType))
			{
				builder.ComplexProperty(
					propertyType,
					property.Name,
					nested => ConfigureGraphRecursive(nested, propertyType, [.. visited])
				);
				continue;
			}

			if (SnapshotTypes.IsPrimitiveLike(propertyType))
			{
				MapReadOnlyConstructorBoundProperty(builder, type, property, propertyType);
				continue;
			}

			throw SnapshotTypes.CreateUnsupportedShapeException(type, property);
		}
	}

	static void MapAsJsonProperty(ISnapshotGraphBuilder builder, Type propertyType, string propertyName) =>
		builder.PropertyWithConversion(
			propertyType,
			propertyName,
			typeof(JsonValueObjectCollectionConverter<>).MakeGenericType(propertyType)
		);

	// EF discovers settable members on its own; a read-only member is only mapped when a constructor
	// parameter can supply it during materialization.
	static void MapReadOnlyConstructorBoundProperty(
		ISnapshotGraphBuilder builder,
		Type containingType,
		PropertyInfo property,
		Type propertyType
	)
	{
		if (
			property.GetSetMethod(true) is null
			&& SnapshotTypes.HasBindableConstructorParameter(containingType, property)
		)
			builder.Property(propertyType, property.Name);
	}

	static void ValidateAggregatePayloadShapeRecursive(Type type, HashSet<Type> visited)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (!SnapshotTypes.ShouldInspectType(type) || !visited.Add(type))
			return;

		foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			if (SnapshotTypes.ShouldSkipJsonProperty(type, property))
				continue;

			var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

			if (propertyType == typeof(string) || propertyType == typeof(byte[]))
				continue;

			if (SnapshotTypes.IsScalarValueObjectType(propertyType))
				continue;

			if (SnapshotTypes.HasEfOpaqueAttribute(property))
				continue;

			if (SnapshotTypes.TryGetEnumerableElementType(propertyType, out var elementType))
			{
				if (
					!SnapshotTypes.IsPrimitiveLike(elementType)
					&& !SnapshotTypes.IsScalarValueObjectType(elementType)
					&& !SnapshotTypes.IsStructValueObjectType(elementType)
				)
				{
					if (!SnapshotTypes.ShouldInspectType(elementType))
						throw SnapshotTypes.CreateUnsupportedShapeException(type, property);

					EnsureValueTypeMemberIsWritable(type, property);
					ValidateAggregatePayloadShapeRecursive(elementType, visited);
				}

				continue;
			}

			if (SnapshotTypes.IsReadOnlyCollectionType(propertyType))
				throw SnapshotTypes.CreateUnsupportedCollectionTypeException(type, property, propertyType);

			if (
				SnapshotTypes.IsDictionaryLikeType(propertyType)
				|| SnapshotTypes.IsJsonConvertibleCollectionType(propertyType)
			)
				continue;

			if (SnapshotTypes.IsCollectionLikeType(propertyType))
				throw SnapshotTypes.CreateUnsupportedCollectionTypeException(type, property, propertyType);

			if (SnapshotTypes.ShouldOwnType(propertyType))
			{
				EnsureValueTypeMemberIsWritable(type, property);
				ValidateAggregatePayloadShapeRecursive(propertyType, visited);
				continue;
			}

			if (!SnapshotTypes.IsPrimitiveLike(propertyType))
				throw SnapshotTypes.CreateUnsupportedShapeException(type, property);
		}
	}

	// EF materializes value-type JSON members through their backing field, and it cannot rebuild an assignment to a
	// read-only member while fixing up a value type, which fails snapshot query compilation with
	// "Expression must be writeable" (analyzer diagnostic EVENTSTOREEF004).
	static void EnsureValueTypeMemberIsWritable(Type type, PropertyInfo property)
	{
		if (type.IsValueType && property.GetSetMethod(nonPublic: true) is null)
			throw SnapshotTypes.CreateUnsupportedValueTypeMemberException(type, property);
	}
}
