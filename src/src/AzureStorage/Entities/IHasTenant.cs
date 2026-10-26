namespace Purview.EventSourcing.AzureStorage.Entities;

/// <summary>
/// A table row that records the tenant owning it.
/// </summary>
interface IHasTenant
{
	string? TenantId { get; set; }
}
