using Azure.Data.Tables;

namespace Purview.EventSourcing.AzureStorage.StorageClients.Table;

sealed class BatchOperation
{
	readonly List<TableTransactionAction> _operations = [];

	public string PartitionKey { get; private set; } = default!;

	public ITableEntity? FailedEntity { get; internal set; }

	public bool Any() => _operations.Count > 0;

	public void Delete<TEntity>(TEntity entity, int? recordAt = null)
		where TEntity : class, ITableEntity, new() => AddAction(TableTransactionActionType.Delete, entity, recordAt);

	public void Add<TEntity>(TEntity entity, int? recordAt = null)
		where TEntity : class, ITableEntity, new() => AddAction(TableTransactionActionType.Add, entity, recordAt);

	public void Update<TEntity>(TEntity entity, bool merge = true, int? recordAt = null)
		where TEntity : class, ITableEntity, new() =>
		AddAction(
			merge ? TableTransactionActionType.UpdateMerge : TableTransactionActionType.UpdateReplace,
			entity,
			recordAt
		);

	public void Upsert<TEntity>(TEntity entity, bool merge = true, int? recordAt = null)
		where TEntity : class, ITableEntity, new() =>
		AddAction(
			merge ? TableTransactionActionType.UpsertMerge : TableTransactionActionType.UpsertReplace,
			entity,
			recordAt
		);

	public void AddAction<TEntity>(TableTransactionActionType actionType, TEntity entity, int? recordAt = null)
		where TEntity : class, ITableEntity, new()
	{
		if (PartitionKey == null)
			PartitionKey = entity.PartitionKey;
		else if (PartitionKey != entity.PartitionKey)
			throw new InvalidPartitionKeyException(PartitionKey, entity.PartitionKey);

		TableTransactionAction tableOperation = new(actionType, entity);
		if (recordAt.HasValue)
			_operations.Insert(recordAt.Value, tableOperation);
		else
			_operations.Add(tableOperation);
	}

	internal IEnumerable<TableTransactionAction> GetActions() => _operations;

	/// <summary>
	/// Resolves the entity behind a failed transaction action so a caller can classify the failure
	/// by the row it collided with.
	/// </summary>
	internal void RecordFailedAction(int? failedActionIndex) =>
		FailedEntity =
			failedActionIndex is int index && index >= 0 && index < _operations.Count
				? _operations[index].Entity
				: null;
}
