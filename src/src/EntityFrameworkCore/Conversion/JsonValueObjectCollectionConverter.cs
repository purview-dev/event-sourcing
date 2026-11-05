using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Purview.EventSourcing.Serialization;

namespace Purview.EventSourcing.EntityFrameworkCore.Conversion;

/// <summary>
/// Persists a collection - or any other member EF cannot map structurally - as a JSON string using the
/// event store's serialization settings.
/// </summary>
/// <typeparam name="TCollection">The collection type.</typeparam>
sealed class JsonValueObjectCollectionConverter<TCollection> : ValueConverter<TCollection, string>
{
	/// <summary>
	/// Creates a new <see cref="JsonValueObjectCollectionConverter{TCollection}"/>.
	/// </summary>
	public JsonValueObjectCollectionConverter()
		: base(
			value => EventStoreSerializationHelpers.Serialize(value),
			value => EventStoreSerializationHelpers.Deserialize<TCollection>(value)!
		)
	{
		//
	}
}
