using Microsoft.Azure.Cosmos;
using Purview.EventSourcing.Serialization;

namespace Purview.EventSourcing.CosmosDb;

partial class CosmosDbClient
{
	public sealed class CosmosSystemTextJsonSerializer : CosmosSerializer
	{
		static readonly System.Text.Json.JsonSerializerOptions JsonSerializerOptions =
			EventStoreSerializationHelpers.CreateJsonSerializerOptions();

		public override T FromStream<T>(Stream stream)
		{
			if (typeof(Stream).IsAssignableFrom(typeof(T)))
				return (T)(object)stream;

			using (stream)
				return System.Text.Json.JsonSerializer.Deserialize<T>(stream, JsonSerializerOptions)!;
		}

		public override Stream ToStream<T>(T input)
		{
			MemoryStream streamPayload = new();
			System.Text.Json.JsonSerializer.Serialize(streamPayload, input, JsonSerializerOptions);

			streamPayload.Position = 0;
			return streamPayload;
		}
	}
}
