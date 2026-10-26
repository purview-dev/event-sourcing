using Purview.EventSourcing.Services;

namespace Purview.EventSourcing.AzureStorage;

// The store is a provider facade over Table Storage, Blob Storage, caching, telemetry and the change
// feed, so its coupling is inherent to the role; adopting the result model added the two failure-model
// namespaces and tipped it two types past the limit.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
	"Maintainability",
	"CA1506:Avoid excessive class coupling",
	Justification = "Provider facade; the coupling is the sum of the storage, caching and telemetry surfaces it owns."
)]
partial class TableEventStore<T>
{
	///<inheritdoc/>
	public async Task<T> CreateAsync(string? aggregateId = null, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(aggregateId))
		{
			if (_aggregateIdFactory != null)
			{
				aggregateId = await _aggregateIdFactory.CreateAsync<T>(cancellationToken);
				if (string.IsNullOrWhiteSpace(aggregateId))
					throw new NullReferenceException(
						$"The {typeof(IAggregateIdFactory).FullName} implementation ({_aggregateIdFactory.GetType().FullName}) generated a null or empty Id."
					);
			}
			else
				aggregateId = $"{Guid.NewGuid()}:D";
		}

		T aggregate = new() { Details = { Id = aggregateId } };

		return FulfilRequirements(aggregate);
	}
}
