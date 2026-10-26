using System.Reflection;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.EntityFrameworkCore.Conversion;

/// <summary>
/// Reflection helpers shared by the scalar value-object converters, resolving the member that carries the
/// scalar value and the factory used to rebuild the value object from it.
/// </summary>
static class ScalarValueObjects
{
	/// <summary>
	/// Gets the property identified by the type's <see cref="ScalarAttribute"/>.
	/// </summary>
	/// <param name="scalarType">The scalar value object type.</param>
	/// <returns>The property carrying the scalar value.</returns>
	public static PropertyInfo GetScalarProperty(Type scalarType)
	{
		var scalarAttribute = GetScalarAttribute(scalarType);

		return scalarType.GetProperty(scalarAttribute.PropertyName, BindingFlags.Instance | BindingFlags.Public)
			?? throw new InvalidOperationException(
				$"'{scalarType.Name}' missing scalar property '{scalarAttribute.PropertyName}'."
			);
	}

	/// <summary>
	/// Finds the static factory used to rebuild the value object, honouring the deserialization mode
	/// declared by the type's <see cref="ScalarAttribute"/>.
	/// </summary>
	/// <param name="scalarType">The scalar value object type.</param>
	/// <param name="scalarPropertyType">The underlying scalar type.</param>
	/// <returns>The factory method, or <see langword="null"/> when the type exposes none.</returns>
	public static MethodInfo? FindFactoryMethod(Type scalarType, Type scalarPropertyType)
	{
		var (preferred, secondary) = GetFactoryNames(scalarType);

		return scalarType.GetMethod(preferred, BindingFlags.Public | BindingFlags.Static, [scalarPropertyType])
			?? scalarType.GetMethod(secondary, BindingFlags.Public | BindingFlags.Static, [scalarPropertyType]);
	}

	/// <summary>
	/// Creates the exception describing the factory members a scalar value object must expose.
	/// </summary>
	/// <param name="scalarType">The scalar value object type.</param>
	/// <param name="scalarPropertyType">The underlying scalar type.</param>
	/// <returns>The exception to throw.</returns>
	public static InvalidOperationException CreateMissingFactoryException(Type scalarType, Type scalarPropertyType)
	{
		var (preferred, secondary) = GetFactoryNames(scalarType);

		return new(
			$"{scalarType.Name} must expose static {preferred}({scalarPropertyType.Name}), static {secondary}({scalarPropertyType.Name}), or ctor({scalarPropertyType.Name})."
		);
	}

	static (string Preferred, string Secondary) GetFactoryNames(Type scalarType)
	{
		var scalarAttribute = GetScalarAttribute(scalarType);
		var preferred =
			scalarAttribute.DeserializationMode == ValueObjectDeserializationMode.Strict ? "Create" : "Hydrate";

		return (preferred, preferred == "Hydrate" ? "Create" : "Hydrate");
	}

	static ScalarAttribute GetScalarAttribute(Type scalarType) =>
		scalarType.GetCustomAttribute<ScalarAttribute>()
		?? throw new InvalidOperationException($"{scalarType.Name} must be annotated with [Scalar].");
}
