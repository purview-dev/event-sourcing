using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Postgres.Client;

sealed class ScalarValueConverter<TScalarObject, TScalar> : ValueConverter<TScalarObject, TScalar>
{
	public ScalarValueConverter()
		: base(BuildToProviderExpression(), BuildFromProviderExpression()) { }

	static Expression<Func<TScalarObject, TScalar>> BuildToProviderExpression()
	{
		var scalarProperty = GetScalarProperty();
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

	static Expression BuildCreatorExpression(ParameterExpression source)
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
			return Expression.Call(create, source);

		var ctor = scalarType.GetConstructor(
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			[scalarPropertyType]
		);

		return ctor is null
			? throw new InvalidOperationException(
				$"{scalarType.Name} must expose static {preferredFactoryName}({scalarPropertyType.Name}), static {secondaryFactoryName}({scalarPropertyType.Name}), or ctor({scalarPropertyType.Name})."
			)
			: (Expression)Expression.New(ctor, source);
	}
}
