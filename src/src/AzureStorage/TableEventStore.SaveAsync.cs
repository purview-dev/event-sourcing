using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing.AzureStorage;

partial class TableEventStore<T>
{
	///<inheritdoc/>
	[DebuggerStepThrough]
	public Task<Result<SaveOutcome<T>, AggregateSaveFailure>> SaveAsync(
		[NotNull] T aggregate,
		EventStoreOperationContext? operationContext,
		CancellationToken cancellationToken = default
	) => _saveOperation.SaveAsync(aggregate, operationContext, cancellationToken);
}
