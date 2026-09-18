namespace Purview.EventSourcing.Samples.ValueObjects;

public enum ReportProcessingStatusCode
{
	None = 0,

	Uploaded = 1000,

	Processing = 2000,

	Completed = 3000,

	Failed = 4000,
}
