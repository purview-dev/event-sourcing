using System.Text.Json;
using System.Text.Json.Serialization;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Serialization;

/// <summary>
/// Provides shared JSON serialization helpers used across the event store framework and its providers.
/// </summary>
/// <remarks>
/// The helpers use a single <see cref="JsonSerializerOptions"/> instance that applies the framework-wide
/// converters, including <see cref="ScalarJsonConverterFactory"/> for scalar value objects and
/// <see cref="StringValuesConverter"/> for <c>StringValues</c>. Providers should reuse these helpers so
/// serialized payload shape remains consistent across storage backends.
/// </remarks>
public static class EventStoreSerializationHelpers
{
	/// <summary>
	/// Gets the shared <see cref="JsonSerializerOptions"/> used for event payload and snapshot serialization.
	/// </summary>
	public static JsonSerializerOptions JsonSerializerOptions { get; } = CreateJsonSerializerOptions();

	/// <summary>
	/// Creates a new <see cref="JsonSerializerOptions"/> instance configured with the framework's converters.
	/// </summary>
	/// <returns>A configured <see cref="JsonSerializerOptions"/> instance.</returns>
	/// <remarks>
	/// Null values are omitted when writing, object creation prefers populating existing instances, and the
	/// output is not indented. The <see cref="ScalarJsonConverterFactory"/> and <see cref="StringValuesConverter"/>
	/// converters are registered.
	/// </remarks>
	public static JsonSerializerOptions CreateJsonSerializerOptions()
	{
		JsonSerializerOptions options = new()
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
			WriteIndented = false,
		};

		options.Converters.Add(new StringValuesConverter());
		options.Converters.Add(new ScalarJsonConverterFactory());

		return options;
	}

	/// <summary>
	/// Serializes the value to a JSON string using the shared options.
	/// </summary>
	/// <typeparam name="T">The type of the value to serialize.</typeparam>
	/// <param name="value">The value to serialize.</param>
	/// <returns>The JSON string representation of <paramref name="value"/>.</returns>
	public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions);

	/// <summary>
	/// Serializes the value to a JSON string using the shared options and the supplied input type.
	/// </summary>
	/// <param name="value">The value to serialize.</param>
	/// <param name="inputType">The runtime type to serialize <paramref name="value"/> as.</param>
	/// <returns>The JSON string representation of <paramref name="value"/>.</returns>
	public static string Serialize(object? value, Type inputType) =>
		JsonSerializer.Serialize(value, inputType, JsonSerializerOptions);

	/// <summary>
	/// Deserializes the JSON string into an instance of <typeparamref name="T"/> using the shared options.
	/// </summary>
	/// <typeparam name="T">The type to deserialize into.</typeparam>
	/// <param name="json">The JSON string to deserialize.</param>
	/// <returns>The deserialized instance, or null when the JSON represents null.</returns>
	public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions);

	/// <summary>
	/// Deserializes the JSON string into an instance of <paramref name="returnType"/> using the shared options.
	/// </summary>
	/// <param name="json">The JSON string to deserialize.</param>
	/// <param name="returnType">The type to deserialize into.</param>
	/// <returns>The deserialized instance, or null when the JSON represents null.</returns>
	public static object? Deserialize(string json, Type returnType) =>
		JsonSerializer.Deserialize(json, returnType, JsonSerializerOptions);

	/// <summary>
	/// Asynchronously deserializes the stream into an instance of <typeparamref name="T"/> using the shared options.
	/// </summary>
	/// <typeparam name="T">The type to deserialize into.</typeparam>
	/// <param name="utf8Json">The UTF-8 JSON stream to read.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The deserialized instance, or null when the stream represents null.</returns>
	public static ValueTask<T?> DeserializeAsync<T>(Stream utf8Json, CancellationToken cancellationToken = default) =>
		JsonSerializer.DeserializeAsync<T>(utf8Json, JsonSerializerOptions, cancellationToken);

	/// <summary>
	/// Asynchronously deserializes the stream into an instance of <paramref name="returnType"/> using the shared options.
	/// </summary>
	/// <param name="utf8Json">The UTF-8 JSON stream to read.</param>
	/// <param name="returnType">The type to deserialize into.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>The deserialized instance, or null when the stream represents null.</returns>
	public static ValueTask<object?> DeserializeAsync(
		Stream utf8Json,
		Type returnType,
		CancellationToken cancellationToken = default
	) => JsonSerializer.DeserializeAsync(utf8Json, returnType, JsonSerializerOptions, cancellationToken);

	/// <summary>
	/// Asynchronously serializes the value to the stream using the shared options.
	/// </summary>
	/// <typeparam name="T">The type of the value to serialize.</typeparam>
	/// <param name="utf8Json">The UTF-8 stream to write to.</param>
	/// <param name="value">The value to serialize.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	public static Task SerializeAsync<T>(Stream utf8Json, T value, CancellationToken cancellationToken = default) =>
		JsonSerializer.SerializeAsync(utf8Json, value, JsonSerializerOptions, cancellationToken);

	/// <summary>
	/// Asynchronously serializes the value to the stream using the shared options and the supplied input type.
	/// </summary>
	/// <param name="utf8Json">The UTF-8 stream to write to.</param>
	/// <param name="value">The value to serialize.</param>
	/// <param name="inputType">The runtime type to serialize <paramref name="value"/> as.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	public static Task SerializeAsync(
		Stream utf8Json,
		object? value,
		Type inputType,
		CancellationToken cancellationToken = default
	) => JsonSerializer.SerializeAsync(utf8Json, value, inputType, JsonSerializerOptions, cancellationToken);
}
