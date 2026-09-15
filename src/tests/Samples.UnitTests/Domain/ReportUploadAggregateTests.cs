using Purview.EventSourcing.Samples.Domain.ReportUpload;
using Purview.EventSourcing.Samples.ValueObjects;

namespace Purview.EventSourcing.Samples.Domain;

public sealed class ReportUploadAggregateTests
{
	static readonly Faker Faker = new();

	[Test]
	public async Task MarkAsComplete_GivenReportIsMarkedAsComplete_SetsStatusToCompleteAsSideEffect()
	{
		// Arrange
		var sut = CreateSUT();
		sut.Create(CreateProjectId(), "report.json", CreateValidBlobUri(), CreateUploadedUser());
		await Assert.That(sut.Status).IsNotEqualTo(ReportProcessingStatus.Completed);

		// Act (status is not passed by caller)
		sut.MarkAsComplete(CreateReportSummary());

		// Assert
		await Assert.That(sut.Status).IsEqualTo(ReportProcessingStatus.Completed);
	}

	[Test]
	public async Task MarkAsComplete_GivenReportIsMarkedAsComplete_RecordsStatusInEvent()
	{
		// Arrange
		var sut = CreateSUT();
		sut.Create(CreateProjectId(), "report.json", CreateValidBlobUri(), CreateUploadedUser());

		// Act
		sut.MarkAsComplete(CreateReportSummary());

		// Assert (event contains the computed status value)
		var completedEvent = sut.GetUnsavedEvents()
			.Single(record => record.Event.GetType().GetProperty("Status") is not null)
			.Event;
		var statusProperty = completedEvent.GetType().GetProperty("Status");
		await Assert.That(statusProperty).IsNotNull();
		await Assert.That(statusProperty!.GetValue(completedEvent)).IsEqualTo(ReportProcessingStatus.Completed);
	}

	[Test]
	public void MarkAsComplete_GivenCallerSetsComputedStatus_ThrowsArgumentException()
	{
		// Arrange
		var sut = CreateSUT();
		sut.Create(CreateProjectId(), "report.json", CreateValidBlobUri(), CreateUploadedUser());

		// Act & Assert
		Assert.Throws<ArgumentException>(() =>
			sut.MarkAsComplete(CreateReportSummary(), status: ReportProcessingStatus.Failed)
		);
	}

	static ReportUploadAggregate CreateSUT() => new();

	static ProjectId CreateProjectId() => ProjectId.Create(Guid.NewGuid().ToString());

	static BlobUri CreateValidBlobUri() =>
		BlobUri.Create(new Uri($"/example/nesting/{Guid.NewGuid()}/blob.json", UriKind.Relative));

	static UserCapture CreateUploadedUser() =>
		UserCapture.Create(UserDetails.Create(Guid.NewGuid(), "Uploader", true), DateTimeOffset.UtcNow);

	static ReportSummary CreateReportSummary()
	{
		return ReportSummary.Create(
			new()
			{
				AssetDetails = new AssetDetails(
					new Dictionary<PlatformID, int>
					{
						{ PlatformID.Win32NT, Faker.Random.Int(1, 100) },
						{ PlatformID.Unix, Faker.Random.Int(1, 100) },
						{ PlatformID.Other, Faker.Random.Int(1, 100) },
					}
				),
				ParserDetails = new(10, 5, 5, TimeSpan.FromMinutes(1)),
				Projects = Faker.Make(2, i => new Project($"Project {i + 1}", $"{i + 1}", $"Team {i + 1}")),
				VulnerabilityDetails = new VulnerabilityDetails(100, 10, 10, 20, 30, 40),
			}
		);
	}
}
