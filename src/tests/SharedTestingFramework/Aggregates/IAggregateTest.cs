using Microsoft.Extensions.Primitives;

namespace Purview.EventSourcing.Aggregates;

public interface IAggregateTest : IAggregate
{
	int IncrementInt32 { get; }

	int Int32Value { get; }

	Guid? OldEventValue { get; }

	string StringProperty { get; }

	Dictionary<string, StringValues> StringValuesDictionary { get; }

	Dictionary<string, string> StringsDictionary { get; }

	ComplexTestType? ComplexTestType { get; }

	void SetValidatedProperty(int value);

	void IncrementInt32Value();

	void SetInt32Value(int value);

	void AppendString(string value);

	void AddKVPs(params KeyValuePair<string, StringValues>[] pairs);

	void AddKVPs(params KeyValuePair<string, string>[] pairs);

	void SetOldEventValue(Guid value);

	void RegisterOldEventType();

	void SetComplexProperty(ComplexTestType complexTestType);
}
