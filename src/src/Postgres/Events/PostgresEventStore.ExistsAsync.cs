namespace Purview.EventSourcing.Postgres.Events;

partial class PostgresEventStore<T>
{
	///<inheritdoc/>
	public async Task<AggregateExistence> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId, nameof(aggregateId));

		var streamVersion = await GetStreamVersionAsync(aggregateId, false, cancellationToken);
		return streamVersion == null
			? AggregateExistence.DoesNotExist
			: new AggregateExistence
			{
				Status = streamVersion.IsDeleted ? ExistenceStatus.ExistsInDeletedState : ExistenceStatus.Exists,
				Version = streamVersion.Version,
			};
	}
}
