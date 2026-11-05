using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Purview.EventSourcing.EntityFrameworkCore.Conversion;

/// <summary>
/// Converts a <c>[Scalar]</c> value object to and from its underlying scalar value, so the member is
/// stored - and queried - as the primitive it wraps.
/// </summary>
/// <typeparam name="TScalarObject">The scalar value object type.</typeparam>
/// <typeparam name="TScalar">The underlying scalar type.</typeparam>
sealed class ScalarValueConverter<TScalarObject, TScalar> : ValueConverter<TScalarObject, TScalar>
{
	/// <summary>
	/// Creates a new <see cref="ScalarValueConverter{TScalarObject, TScalar}"/>.
	/// </summary>
	public ScalarValueConverter()
		: base(BuildToProviderExpression(), BuildFromProviderExpression()) { }

	static Expression<Func<TScalarObject, TScalar>> BuildToProviderExpression()
	{
		var scalarProperty = ScalarValueObjects.GetScalarProperty(typeof(TScalarObject));
		var source = Expression.Parameter(typeof(TScalarObject), "value");
		var body = Expression.Property(source, scalarProperty);
		return Expression.Lambda<Func<TScalarObject, TScalar>>(body, source);
	}

	static Expression<Func<TScalar, TScalarObject>> BuildFromProviderExpression()
	{
		var source = Expression.Parameter(typeof(TScalar), "value");
		var body = BuildCreatorExpression(source);
		return Expression.Lambda<Func<TScalar, TScalarObject>>(body, source);
	}

	static Expression BuildCreatorExpression(ParameterExpression source)
	{
		var scalarType = typeof(TScalarObject);
		var scalarPropertyType = typeof(TScalar);

		var create = ScalarValueObjects.FindFactoryMethod(scalarType, scalarPropertyType);
		if (create is not null)
			return Expression.Call(create, source);

		var ctor = scalarType.GetConstructor(
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			[scalarPropertyType]
		);

		return ctor is null
			? throw ScalarValueObjects.CreateMissingFactoryException(scalarType, scalarPropertyType)
			: (Expression)Expression.New(ctor, source);
	}
}
