using System.Collections.ObjectModel;
using Purview.EventSourcing.Aggregates.Test.Events;

namespace Purview.EventSourcing.Aggregates.Test;

public class TestAggregate : AggregateBase
{
	public bool EventRecorded { get; private set; }

	public int IncrementedValue { get; private set; }

	public IReadOnlyDictionary<string, IEnumerable<string>> ReadOnlyDictionary { get; private set; } =
		new ReadOnlyDictionary<string, IEnumerable<string>>(new Dictionary<string, IEnumerable<string>>());

	public string? DoNotTouchCasePropertyValue { get; private set; }

	protected override void RegisterEvents()
	{
		Register<RecordEventTestEvent>(Apply);
		Register<IncrementEvent>(Apply);
		Register<AppendToReadOnlyDictionaryEvent>(Apply);
		Register<PropertyBaseExpressionEvent>(Apply);
	}

	void Apply(PropertyBaseExpressionEvent obj)
	{
		if (obj.PropertyName == nameof(DoNotTouchCasePropertyValue))
			DoNotTouchCasePropertyValue = obj.PropertyValue;
	}

	void Apply(AppendToReadOnlyDictionaryEvent obj)
	{
		Dictionary<string, IEnumerable<string>> dict = new(ReadOnlyDictionary.ToDictionary(m => m.Key, m => m.Value))
		{
			{ obj.Key, obj.Values },
		};

		ReadOnlyDictionary = dict;
	}

	void Apply(IncrementEvent obj) => IncrementedValue++;

	void Apply(RecordEventTestEvent obj) => EventRecorded = true;

	public void RecordEvent() => RecordAndApply(new RecordEventTestEvent());

	public void Increment() => RecordAndApply(new IncrementEvent());

	public void Append(string key, IEnumerable<string> values)
	{
		RecordAndApply(new AppendToReadOnlyDictionaryEvent { Key = key, Values = values });
	}

	public TestAggregate ChangeDoNotTouchCase(string? propertyValue) =>
		this.CompareRecordAndApply(
			_ => DoNotTouchCasePropertyValue,
			propertyValue,
			p => new PropertyBaseExpressionEvent
			{
				PropertyValue = p,
				PropertyName = nameof(DoNotTouchCasePropertyValue),
			}
		);
}
