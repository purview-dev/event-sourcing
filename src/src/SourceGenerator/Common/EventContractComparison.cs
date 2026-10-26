namespace Purview.EventSourcing.SourceGenerator.Common;

/// <summary>
/// Compares the current event contracts against an approved baseline manifest and produces
/// precise compatibility diagnostics. Compatible additive changes (new aggregates, new events,
/// additive optional fields, schema-version bumps) are deliberately silent.
/// </summary>
static class EventContractComparer
{
	public static readonly EventContractComparison Empty = new(new([]));

	public static EventContractComparison Compare(EventContractManifest current, BaselineState baselineState)
	{
		if (baselineState.Error is not null)
		{
			return new(
				new([
					new ContractIssue(
						DiagnosticLibrary.EventContractBaselineMalformed,
						new([EventContractManifestLibrary.BaselineFileName, baselineState.Error.Message]),
						EventKey: null,
						AggregateKey: null
					),
				])
			);
		}

		if (baselineState.Manifest is null)
			return Empty;

		var baseline = baselineState.Manifest;
		List<ContractIssue> issues = [];
		Dictionary<(string Namespace, string Name), AggregateContract> currentAggregates = [];
		foreach (var aggregate in current.Aggregates)
			currentAggregates[(aggregate.AggregateNamespace, aggregate.AggregateName)] = aggregate;

		foreach (var baselineAggregate in baseline.Aggregates)
		{
			if (
				!currentAggregates.TryGetValue(
					(baselineAggregate.AggregateNamespace, baselineAggregate.AggregateName),
					out var currentAggregate
				)
			)
			{
				issues.Add(
					Issue(
						DiagnosticLibrary.EventContractRemoved,
						[baselineAggregate.AggregateName],
						eventKey: null,
						aggregateKey: null
					)
				);
				continue;
			}

			CompareAggregate(currentAggregate, baselineAggregate, issues);
		}

		return new(new([.. issues]));
	}

	static void CompareAggregate(
		AggregateContract currentAggregate,
		AggregateContract baselineAggregate,
		List<ContractIssue> issues
	)
	{
		var aggregateKey = EventContractManifestLibrary.CreateAggregateKey(
			baselineAggregate.AggregateNamespace,
			baselineAggregate.AggregateName
		);

		Dictionary<(string Name, string Namespace, int Version), EventContractEntry> currentEvents = [];
		Dictionary<(string Name, string Namespace), List<EventContractEntry>> currentByName = [];
		foreach (var entry in currentAggregate.Events)
		{
			currentEvents[(entry.EventName, entry.EventNamespace, entry.SchemaVersion)] = entry;
			var nameKey = (entry.EventName, entry.EventNamespace);
			if (!currentByName.TryGetValue(nameKey, out var list))
			{
				list = [];
				currentByName[nameKey] = list;
			}

			list.Add(entry);
		}

		foreach (var baselineEvent in baselineAggregate.Events)
		{
			var identity = (baselineEvent.EventName, baselineEvent.EventNamespace, baselineEvent.SchemaVersion);
			if (currentEvents.TryGetValue(identity, out var currentEvent))
			{
				CompareEvent(currentAggregate, currentEvent, baselineEvent, aggregateKey, issues);
				continue;
			}

			if (currentByName.TryGetValue((baselineEvent.EventName, baselineEvent.EventNamespace), out var sameName))
			{
				var maxCurrentVersion = 0;
				foreach (var entry in sameName)
					maxCurrentVersion = Math.Max(maxCurrentVersion, entry.SchemaVersion);

				if (maxCurrentVersion > baselineEvent.SchemaVersion)
					continue;

				issues.Add(
					Issue(
						DiagnosticLibrary.EventContractSchemaVersionRegression,
						[
							baselineEvent.EventName,
							baselineAggregate.AggregateName,
							maxCurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
							baselineEvent.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
						],
						eventKey: EventContractManifestLibrary.CreateEventKey(
							baselineAggregate.AggregateNamespace,
							baselineAggregate.AggregateName,
							sameName[0].MethodName
						),
						aggregateKey: aggregateKey
					)
				);
				continue;
			}

			issues.Add(
				Issue(
					DiagnosticLibrary.EventContractEventRemoved,
					[
						baselineEvent.EventName,
						baselineEvent.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
						baselineAggregate.AggregateName,
					],
					eventKey: null,
					aggregateKey: aggregateKey
				)
			);
		}
	}

	static void CompareEvent(
		AggregateContract aggregate,
		EventContractEntry currentEvent,
		EventContractEntry baselineEvent,
		string aggregateKey,
		List<ContractIssue> issues
	)
	{
		var eventKey = EventContractManifestLibrary.CreateEventKey(
			aggregate.AggregateNamespace,
			aggregate.AggregateName,
			currentEvent.MethodName
		);
		var version = baselineEvent.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

		Dictionary<string, EventContractField> currentFields = new(StringComparer.Ordinal);
		foreach (var field in currentEvent.Fields)
			currentFields[field.Name] = field;

		foreach (var baselineField in baselineEvent.Fields)
		{
			if (!currentFields.TryGetValue(baselineField.Name, out var currentField))
			{
				issues.Add(
					Issue(
						DiagnosticLibrary.EventContractFieldRemoved,
						[baselineField.Name, baselineEvent.EventName, version, aggregate.AggregateName],
						eventKey: eventKey,
						aggregateKey: aggregateKey
					)
				);
				continue;
			}

			if (!FieldTypesEqual(baselineField, currentField))
			{
				issues.Add(
					Issue(
						DiagnosticLibrary.EventContractFieldTypeChanged,
						[
							baselineField.Name,
							baselineEvent.EventName,
							version,
							aggregate.AggregateName,
							baselineField.Type,
							currentField.Type,
						],
						eventKey: eventKey,
						aggregateKey: aggregateKey
					)
				);
				continue;
			}

			if (FieldBecameRequired(baselineField, currentField))
			{
				issues.Add(
					Issue(
						DiagnosticLibrary.EventContractFieldBecameRequired,
						[baselineField.Name, baselineEvent.EventName, version, aggregate.AggregateName],
						eventKey: eventKey,
						aggregateKey: aggregateKey
					)
				);
			}
		}

		foreach (var currentField in currentEvent.Fields)
		{
			if (baselineEvent.Fields.Any(field => StringComparer.Ordinal.Equals(field.Name, currentField.Name)))
				continue;

			// An added field on an unchanged identity/version is compatible unless it is
			// explicitly [Required]: a required addition breaks deserialization of old payloads.
			if (currentField.IsRequired)
			{
				issues.Add(
					Issue(
						DiagnosticLibrary.EventContractFieldBecameRequired,
						[currentField.Name, baselineEvent.EventName, version, aggregate.AggregateName],
						eventKey: eventKey,
						aggregateKey: aggregateKey
					)
				);
			}
		}
	}

	static bool FieldTypesEqual(EventContractField baseline, EventContractField current) =>
		StringComparer.Ordinal.Equals(baseline.Type, current.Type)
		&& StringComparer.Ordinal.Equals(baseline.ElementType, current.ElementType)
		&& baseline.IsArray == current.IsArray;

	static bool FieldBecameRequired(EventContractField baseline, EventContractField current) =>
		(baseline.IsNullable && !current.IsNullable) || (!baseline.IsRequired && current.IsRequired);

	static ContractIssue Issue(
		DiagnosticDescriptor descriptor,
		string[] messageArgs,
		string? eventKey,
		string? aggregateKey
	) => new(descriptor, new([.. messageArgs]), eventKey, aggregateKey);
}
