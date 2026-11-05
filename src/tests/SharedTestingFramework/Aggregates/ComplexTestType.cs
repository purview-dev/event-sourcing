namespace Purview.EventSourcing.Aggregates;

public class ComplexTestType
{
	public short Int16Property { get; set; } = 8999;

	public int Int32Property { get; set; } = 899998899;

	public long Int64Property { get; set; } = 8999132131231230010L;

	public string StringProperty { get; set; } = $"{Guid.NewGuid()}";

	public DateTimeOffset DateTimeOffsetProperty { get; set; } = DateTimeOffset.UtcNow.AddYears(1000);

	public ComplexNestedTestType ComplexNestedTestTypeProperty { get; set; } = new();

	public ComplexNestedTestType? NullableComplexNestedTestTypeProperty { get; set; }
}
