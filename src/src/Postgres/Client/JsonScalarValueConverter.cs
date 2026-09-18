using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Postgres.Client;

/// <summary>
/// Converts a <see cref="ScalarAttribute"/>-annotated value object whose scalar property is a complex type
/// to and from its JSON-serialized string representation for storage in PostgreSQL.
/// </summary>
/// <typeparam name="TScalarObject">The value object type annotated with <see cref="ScalarAttribute"/>.</typeparam>
/// <typeparam name="TScalar">The scalar property type of <typeparamref name="TScalarObject"/>.</typeparam>
sealed class JsonScalarValueConverter<TScalarObject, TScalar> : ValueConverter<TScalarObject, string>
{
	static readonly PropertyInfo ScalarProperty = GetScalarProperty();
	static readonly Func<TScalar, TScalarObject> Creator = BuildCreator();

	/// <summary>
	/// Creates a new <see cref="JsonScalarValueConverter{TScalarObject, TScalar}"/>.
	/// </summary>
	public JsonScalarValueConverter()
		: base(BuildToProviderExpression(), BuildFromProviderExpression()) { }

	static Expression<Func<TScalarObject, string>> BuildToProviderExpression()
	{
		var source = Expression.Parameter(typeof(TScalarObject), "value");
		var serializeMethod = typeof(JsonScalarValueConverter<TScalarObject, TScalar>).GetMethod(
			nameof(Serialize),
			BindingFlags.Static | BindingFlags.NonPublic
		)!;
		var body = Expression.Call(serializeMethod, source);
		return Expression.Lambda<Func<TScalarObject, string>>(body, source);
	}

	static Expression<Func<string, TScalarObject>> BuildFromProviderExpression()
	{
		var source = Expression.Parameter(typeof(string), "value");
		var deserializeMethod = typeof(JsonScalarValueConverter<TScalarObject, TScalar>).GetMethod(
			nameof(Deserialize),
			BindingFlags.Static | BindingFlags.NonPublic
		)!;
		var body = Expression.Call(deserializeMethod, source);
		return Expression.Lambda<Func<string, TScalarObject>>(body, source);
	}

	static string Serialize(TScalarObject value)
	{
		if (value is null)
			return null!;

		var scalarValue = (TScalar)ScalarProperty.GetValue(value)!;
		return JsonSerializer.Serialize(scalarValue);
	}

	static TScalarObject Deserialize(string value)
	{
		if (value is null)
			return default!;

		var scalarValue = JsonSerializer.Deserialize<TScalar>(value)!;
		return Creator(scalarValue);
	}

	static PropertyInfo GetScalarProperty()
	{
		var scalarType = typeof(TScalarObject);
		var scalarAttribute =
			scalarType.GetCustomAttribute<ScalarAttribute>()
			?? throw new InvalidOperationException($"{scalarType.Name} must be annotated with [Scalar].");

		return scalarType.GetProperty(scalarAttribute.PropertyName, BindingFlags.Instance | BindingFlags.Public)
			?? throw new InvalidOperationException(
				$"'{scalarType.Name}' missing scalar property '{scalarAttribute.PropertyName}'."
			);
	}

	static Func<TScalar, TScalarObject> BuildCreator()
	{
		var scalarType = typeof(TScalarObject);
		var scalarPropertyType = typeof(TScalar);
		var scalarAttribute =
			scalarType.GetCustomAttribute<ScalarAttribute>()
			?? throw new InvalidOperationException($"{scalarType.Name} must be annotated with [Scalar].");

		var preferredFactoryName =
			scalarAttribute.DeserializationMode == ValueObjectDeserializationMode.Strict ? "Create" : "Hydrate";
		var secondaryFactoryName = preferredFactoryName == "Hydrate" ? "Create" : "Hydrate";

		var create =
			scalarType.GetMethod(preferredFactoryName, BindingFlags.Public | BindingFlags.Static, [scalarPropertyType])
			?? scalarType.GetMethod(
				secondaryFactoryName,
				BindingFlags.Public | BindingFlags.Static,
				[scalarPropertyType]
			);

		if (create is not null)
			return value => (TScalarObject)create.Invoke(null, [value])!;

		var ctor =
			scalarType.GetConstructor(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				[scalarPropertyType]
			)
			?? throw new InvalidOperationException(
				$"{scalarType.Name} must expose static {preferredFactoryName}({scalarPropertyType.Name}), static {secondaryFactoryName}({scalarPropertyType.Name}), or ctor({scalarPropertyType.Name})."
			);

		// If we reach here, we have a constructor that takes the scalar property type
		return value => (TScalarObject)ctor.Invoke([value]);
	}
}
