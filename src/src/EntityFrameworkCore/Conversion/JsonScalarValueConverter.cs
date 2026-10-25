using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Purview.EventSourcing.EntityFrameworkCore.Conversion;

/// <summary>
/// Converts a <c>[Scalar]</c> value object whose underlying value is not primitive-like to and from its
/// JSON representation.
/// </summary>
/// <typeparam name="TScalarObject">The scalar value object type.</typeparam>
/// <typeparam name="TScalar">The underlying scalar type.</typeparam>
sealed class JsonScalarValueConverter<TScalarObject, TScalar> : ValueConverter<TScalarObject, string>
{
	static readonly PropertyInfo ScalarProperty = ScalarValueObjects.GetScalarProperty(typeof(TScalarObject));
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

	static Func<TScalar, TScalarObject> BuildCreator()
	{
		var scalarType = typeof(TScalarObject);
		var scalarPropertyType = typeof(TScalar);

		var create = ScalarValueObjects.FindFactoryMethod(scalarType, scalarPropertyType);
		if (create is not null)
			return value => (TScalarObject)create.Invoke(null, [value])!;

		var ctor =
			scalarType.GetConstructor(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				[scalarPropertyType]
			) ?? throw ScalarValueObjects.CreateMissingFactoryException(scalarType, scalarPropertyType);

		return value => (TScalarObject)ctor.Invoke([value]);
	}
}
