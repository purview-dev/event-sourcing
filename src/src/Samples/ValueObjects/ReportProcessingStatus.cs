using Purview.EventSourcing.Samples.Domain.ReportUpload;
using Purview.ValueObjects;
using Purview.ValueObjects.Serialization;

namespace Purview.EventSourcing.Samples.ValueObjects;

[Scalar]
public readonly partial record struct ReportProcessingStatus
	: IContextualValueObject<ReportProcessingStatus, ReportProcessingStatusCode, ReportUploadAggregate>
{
	public ReportProcessingStatusCode Value { get; }

	ReportProcessingStatus(ReportProcessingStatusCode code) => Value = code;

	public static ReportProcessingStatus Create(
		ReportProcessingStatusCode value,
		in ValueObjectContext<ReportUploadAggregate> context
	)
	{
		var current = context.Owner.Status;
		return IsValidTransition(current, value)
			? new(value)
			: throw new InvalidOperationException($"Invalid status transition from {current} to {value}");
	}

	static bool IsValidTransition(ReportProcessingStatusCode from, ReportProcessingStatusCode to) =>
		(from, to) switch
		{
			// Uploaded→Uploaded is the only allowed same-status transition (aggregate initialisation)
			(ReportProcessingStatusCode.Uploaded, ReportProcessingStatusCode.Uploaded) => true,

			(ReportProcessingStatusCode.Uploaded, ReportProcessingStatusCode.Processing) => true,
			(ReportProcessingStatusCode.Processing, ReportProcessingStatusCode.Completed) => true,
			(ReportProcessingStatusCode.Processing, ReportProcessingStatusCode.Failed) => true,
			_ => false,
		};
}
