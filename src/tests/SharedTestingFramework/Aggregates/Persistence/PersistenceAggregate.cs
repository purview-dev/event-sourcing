using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Primitives;
using Purview.EventSourcing.Aggregates.Persistence.Events;

namespace Purview.EventSourcing.Aggregates.Persistence;

public sealed class PersistenceAggregate : AggregateBase, IAggregateTest
{
	[Range(0, int.MaxValue)]
	[JsonInclude]
	public int IncrementInt32 { get; private set; }

	[JsonInclude]
	public int Int32Value { get; private set; }

	[JsonInclude]
	public Guid? OldEventValue { get; private set; }

	[JsonInclude]
	public string StringProperty { get; private set; } = default!;

	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	public Dictionary<string, StringValues> StringValuesDictionary { get; } = [];

	[JsonInclude]
	[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
	public Dictionary<string, string> StringsDictionary { get; } = [];

	[JsonInclude]
	public ComplexTestType? ComplexTestType { get; private set; }

	[JsonInclude]
	public string? SubjectName { get; private set; }

	public void SetSubjectName(string name, string dataSubjectId) =>
		RecordAndApply(new SubjectNameSetEvent { Name = name, DataSubjectId = dataSubjectId });

	public void RegisterOldEventType()
	{
		Register<OldEvent>(Apply);
	}

	protected override void RegisterEvents()
	{
		Register<Int32ValueIncrementedEvent>(_ => IncrementInt32++);
		Register<IncrementValueSetEvent>(@event => IncrementInt32 = @event.Value);
		Register<Int32ValueSetEvent>(@event => Int32Value = @event.Value);
		Register<StringValueSetEvent>(@event => StringProperty += @event.Value);
		Register<ComplexPropertySetEvent>(@event => ComplexTestType = @event.ComplexProperty);
		Register<StringValueKVPsAddedEvent>(Apply);
		Register<KVPsAddedEvent>(Apply);
		Register<SubjectNameSetEvent>(@event => SubjectName = @event.Name);
	}

	void Apply(StringValueKVPsAddedEvent obj)
	{
		foreach (var kvp in obj.KVPs)
			StringValuesDictionary.Add(kvp.Key, kvp.Value);
	}

	void Apply(KVPsAddedEvent obj)
	{
		foreach (var kvp in obj.KVPs)
			StringsDictionary.Add(kvp.Key, kvp.Value);
	}

	void Apply(OldEvent @event) => OldEventValue = @event.Value;

	public void SetValidatedProperty(int value) => RecordAndApply(new IncrementValueSetEvent { Value = value });

	public void IncrementInt32Value() => RecordAndApply(new Int32ValueIncrementedEvent());

	public void SetInt32Value(int value)
	{
		if (Int32Value != value)
			RecordAndApply(new Int32ValueSetEvent { Value = value });
	}

	public void AppendString(string value)
	{
		RecordAndApply(new StringValueSetEvent { Value = value });
	}

	public void AddKVPs(params KeyValuePair<string, StringValues>[] pairs) =>
		RecordAndApply(new StringValueKVPsAddedEvent { KVPs = pairs });

	public void AddKVPs(params KeyValuePair<string, string>[] pairs) =>
		RecordAndApply(new KVPsAddedEvent { KVPs = pairs });

	public void SetOldEventValue(Guid value)
	{
		if (value == Guid.Empty)
			throw new ArgumentException("Don't use an empty guid, just for clarity.");

		RecordAndApply(new OldEvent { Value = value });
	}

	public void SetComplexProperty(ComplexTestType complexTestType) =>
		RecordAndApply(new ComplexPropertySetEvent { ComplexProperty = complexTestType });
}
