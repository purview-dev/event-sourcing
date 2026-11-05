using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Purview.EventSourcing.Serialization;

namespace Purview.EventSourcing.EntityFrameworkCore.Conversion;

/// <summary>
/// Persists a member marked <c>[EFOpaque]</c> as a JSON string, keeping it out of query translation.
/// </summary>
/// <typeparam name="TValue">The member type.</typeparam>
sealed class OpaqueJsonValueConverter<TValue> : ValueConverter<TValue, string>
{
	/// <summary>
	/// Creates a new <see cref="OpaqueJsonValueConverter{TValue}"/>.
	/// </summary>
	public OpaqueJsonValueConverter()
		: base(ToJsonExpression(), FromJsonExpression()) { }

	static Expression<Func<TValue, string>> ToJsonExpression() =>
		value => EventStoreSerializationHelpers.Serialize(value);

	static Expression<Func<string, TValue>> FromJsonExpression() =>
		value => EventStoreSerializationHelpers.Deserialize<TValue>(value)!;
}
