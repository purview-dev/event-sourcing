using System.Reflection;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.EntityFrameworkCore.Mapping;

/// <summary>
/// The type-shape rules shared by every Entity Framework snapshot provider: which members are mapped,
/// which are skipped, and which shapes cannot be persisted at all.
/// </summary>
static class SnapshotTypes
{
	// Must match the attribute emitted by Purview.EventSourcing.EntityFrameworkCore.SourceGenerator
	// (TypeLibrary.EFOpaqueAttribute) and applied by consumers as [EFOpaque].
	const string EFOpaqueAttributeFullName = "Purview.EventSourcing.EntityFrameworkCore.EFOpaqueAttribute";

	/// <summary>Determines whether a member is marked <c>[EFOpaque]</c>.</summary>
	/// <param name="member">The member.</param>
	/// <returns><see langword="true"/> when the member is opaque.</returns>
	public static bool HasEfOpaqueAttribute(MemberInfo member) =>
		member.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == EFOpaqueAttributeFullName);

	/// <summary>Determines whether a type should be walked rather than treated as a leaf.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> when the type should be inspected.</returns>
	public static bool ShouldInspectType(Type type) =>
		!type.IsPrimitive
		&& !type.IsEnum
		&& type != typeof(string)
		&& type != typeof(decimal)
		&& type != typeof(Guid)
		&& type != typeof(DateTime)
		&& type != typeof(DateTimeOffset)
		&& type != typeof(DateOnly)
		&& type != typeof(TimeOnly)
		&& type != typeof(TimeSpan)
		&& (type.Namespace is null || !type.Namespace.StartsWith("System", StringComparison.Ordinal));

	/// <summary>Determines whether a type should be mapped as a nested complex type.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> when the type should be owned.</returns>
	public static bool ShouldOwnType(Type type) =>
		ShouldInspectType(type) && (type.IsClass || (type.IsValueType && !type.IsPrimitive && !type.IsEnum));

	/// <summary>Determines whether a member is excluded from the snapshot payload.</summary>
	/// <param name="containingType">The declaring type.</param>
	/// <param name="property">The member.</param>
	/// <returns><see langword="true"/> when the member is not persisted.</returns>
	public static bool ShouldSkipJsonProperty(Type containingType, PropertyInfo property) =>
		property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is not null
		|| property.GetGetMethod(true) is null
		|| (property.GetSetMethod(true) is null && !HasBindableConstructorParameter(containingType, property));

	/// <summary>Determines whether a member is bound through a constructor parameter.</summary>
	/// <param name="containingType">The declaring type.</param>
	/// <param name="property">The member.</param>
	/// <returns><see langword="true"/> when a matching constructor parameter exists.</returns>
	public static bool HasBindableConstructorParameter(Type containingType, PropertyInfo property)
	{
		var constructors = containingType.GetConstructors(
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);
		foreach (var constructor in constructors)
		{
			foreach (var parameter in constructor.GetParameters())
			{
				if (
					string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase)
					&& parameter.ParameterType == property.PropertyType
				)
					return true;
			}
		}

		return false;
	}

	/// <summary>Determines whether a type is stored as a single scalar value.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> when the type is primitive-like.</returns>
	public static bool IsPrimitiveLike(Type type) =>
		type.IsPrimitive
		|| type.IsEnum
		|| type == typeof(string)
		|| type == typeof(decimal)
		|| type == typeof(Uri)
		|| type == typeof(Guid)
		|| type == typeof(DateTime)
		|| type == typeof(DateTimeOffset)
		|| type == typeof(DateOnly)
		|| type == typeof(TimeOnly)
		|| type == typeof(TimeSpan)
		|| type == typeof(byte[]);

	/// <summary>Determines whether a type is a struct value object.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> when the type is a value-type value object.</returns>
	public static bool IsStructValueObjectType(Type type) =>
		type.IsValueType && type.GetCustomAttribute<ValueObjectAttribute>() is not null;

	/// <summary>Determines whether a type is a scalar value object.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> when the type carries <see cref="ScalarAttribute"/>.</returns>
	public static bool IsScalarValueObjectType(Type type) => type.GetCustomAttribute<ScalarAttribute>() is not null;

	/// <summary>Gets the element type of an event-store collection member.</summary>
	/// <param name="type">The member type.</param>
	/// <param name="elementType">The element type, when the member is an event-store collection.</param>
	/// <returns><see langword="true"/> when an element type was resolved.</returns>
	public static bool TryGetEnumerableElementType(Type type, out Type elementType)
	{
		elementType = null!;

		if (!type.IsGenericType)
			return false;

		if (!IsEventStoreCollectionType(type))
			return false;

		elementType = type.GetGenericArguments()[0];
		return true;
	}

	/// <summary>Determines whether a type is an <c>EventStoreList&lt;T&gt;</c> or <c>EventStoreSet&lt;T&gt;</c>.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> for event-store collections.</returns>
	public static bool IsEventStoreCollectionType(Type type) =>
		type.IsGenericType
		&& type.GetGenericTypeDefinition() is var genericDefinition
		&& (genericDefinition == typeof(EventStoreList<>) || genericDefinition == typeof(EventStoreSet<>));

	/// <summary>Determines whether a type is a collection of any kind.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> for collection-like types.</returns>
	public static bool IsCollectionLikeType(Type type)
	{
		if (type == typeof(string) || type == typeof(byte[]))
			return false;

		if (type.IsArray)
			return true;

		if (!type.IsGenericType)
			return false;

		// Test for our own collection types or if the type implements IEnumerable<T> somewhere...
		return IsEventStoreCollectionType(type)
			|| type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
	}

	/// <summary>Determines whether a type is a read-only collection interface.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> for read-only collection interfaces.</returns>
	public static bool IsReadOnlyCollectionType(Type type)
	{
		if (!type.IsGenericType)
			return false;

		var genericTypeDefinition = type.GetGenericTypeDefinition();
		return genericTypeDefinition == typeof(IReadOnlyList<>)
			|| genericTypeDefinition == typeof(IReadOnlyCollection<>);
	}

	/// <summary>Determines whether a type is a dictionary.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> for dictionary-like types.</returns>
	public static bool IsDictionaryLikeType(Type type)
	{
		if (!type.IsGenericType)
			return false;

		var genericTypeDefinition = type.GetGenericTypeDefinition();
		if (
			genericTypeDefinition == typeof(Dictionary<,>)
			|| genericTypeDefinition == typeof(IDictionary<,>)
			|| genericTypeDefinition == typeof(IReadOnlyDictionary<,>)
		)
			return true;

		// Test if the type implements IDictionary<TKey, TValue> or IReadOnlyDictionary<TKey, TValue> somewhere...
		return type.GetInterfaces()
			.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
	}

	/// <summary>Determines whether a collection can be persisted as JSON rather than structurally.</summary>
	/// <param name="type">The type.</param>
	/// <returns><see langword="true"/> when the collection is JSON-convertible.</returns>
	public static bool IsJsonConvertibleCollectionType(Type type)
	{
		if (type == typeof(string) || type == typeof(byte[]))
			return false;

		if (!type.IsGenericType || IsEventStoreCollectionType(type) || IsDictionaryLikeType(type))
			return false;

		if (type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			return true;

		// Test if the type implements IEnumerable<T> somewhere...
		return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
	}

	/// <summary>Creates the exception describing an unmappable member.</summary>
	/// <param name="containingType">The declaring type.</param>
	/// <param name="member">The member.</param>
	/// <returns>The exception to throw.</returns>
	public static InvalidOperationException CreateUnsupportedShapeException(Type containingType, MemberInfo member) =>
		new(
			$"{containingType.Name}.{member.Name} cannot be mapped into the snapshot JSON payload. "
				+ "Supported members are primitive types, [Scalar] value objects, complex types, and EventStoreList<T>/EventStoreSet<T> collections of those shapes."
		);

	/// <summary>Creates the exception describing an unsupported collection member.</summary>
	/// <param name="containingType">The declaring type.</param>
	/// <param name="member">The member.</param>
	/// <param name="collectionType">The collection type.</param>
	/// <returns>The exception to throw.</returns>
	public static InvalidOperationException CreateUnsupportedCollectionTypeException(
		Type containingType,
		MemberInfo member,
		Type collectionType
	) =>
		new(
			$"{containingType.Name}.{member.Name} uses unsupported collection type '{collectionType.Name}'. "
				+ "Collection and array members must use Purview.EventSourcing.EventStoreList<T> or Purview.EventSourcing.EventStoreSet<T>."
		);

	/// <summary>Creates the exception describing a read-only member of a value type.</summary>
	/// <param name="containingType">The declaring type.</param>
	/// <param name="member">The member.</param>
	/// <returns>The exception to throw.</returns>
	public static InvalidOperationException CreateUnsupportedValueTypeMemberException(
		Type containingType,
		MemberInfo member
	) =>
		new(
			$"{containingType.Name}.{member.Name} is a read-only complex member of value type '{containingType.Name}' and cannot be assigned during snapshot JSON materialization, which fails queries with 'Expression must be writeable'. "
				+ "Add an 'init' or 'set' accessor, mark the member [JsonIgnore] to exclude it from the snapshot, or mark it [EFOpaque] to persist it as non-queryable JSON."
		);
}
