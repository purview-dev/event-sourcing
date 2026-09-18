using Purview.EventSourcing.Samples.Domain.ReportUpload;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[ValueObject]
public sealed partial record class ReportSummary
{
	public ParserReportSummary Value { get; }
}
