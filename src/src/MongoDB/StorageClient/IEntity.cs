namespace Purview.EventSourcing.MongoDB.StorageClient;

interface IEntity
{
	string Id { get; set; }

	string AggregateId { get; set; }

	int EntityType { get; }

	string? TenantId { get; set; }
}
