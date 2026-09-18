using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[ValueObject]
public sealed partial record StatusHistory(ReportProcessingStatus Status, DateTimeOffset Timestamp, UserCapture User);
