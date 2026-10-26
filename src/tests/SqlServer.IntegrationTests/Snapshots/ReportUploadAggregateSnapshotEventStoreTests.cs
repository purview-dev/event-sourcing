using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Fixtures.SqlServer;
using Purview.EventSourcing.Samples.Domain.ReportUpload;
using Purview.EventSourcing.Samples.ValueObjects;

namespace Purview.EventSourcing.SqlServer.Snapshots;

[ClassDataSource<SqlServerSnapshotEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class ReportUploadAggregateSnapshotEventStoreTests(SqlServerSnapshotEventStoreFixture fixture)
{
	static readonly Faker Faker = new();

	static ReportSummary CreateReportSummary() =>
		ReportSummary.Create(
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

	static ReportUploadAggregate CreateCompletedAggregate(string id, ReportSummary? reportSummary = null) =>
		TestHelpers.Aggregate<ReportUploadAggregate>(
			id,
			agg =>
				agg.Create(
						"a-file-name.json",
						"213123",
						BlobUri.Create(new Uri("/a/path/to/the/original/json", UriKind.Relative)),
						UserCapture.Create(
							UserDetails.Create(Guid.NewGuid(), "Testing Account", true),
							DateTimeOffset.UtcNow
						)
					)
					.AddExcelReport(
						GuidObjectId.Create(Guid.NewGuid()),
						BlobUri.Create(new Uri("/a/path/to/an/object", UriKind.Relative))
					)
					.MarkAsComplete(reportSummary ?? CreateReportSummary()),
			clearEvents: false
		);

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Provider integration scenario intentionally exercises the complete aggregate graph."
	)]
	public async Task SnapshotAsync_GivenReportUploadAggregateWithLineItems_QueriesByLineItemCount(
		CancellationToken cancellationToken
	)
	{
		var store = fixture.CreateSnapshotStore<ReportUploadAggregate>();
		var id = Guid.NewGuid().ToString("D");
		var aggregate = CreateCompletedAggregate(id);

		await store.SaveAsync(aggregate, cancellationToken);

		var restoredResult = await store.GetAsync(id, cancellationToken: cancellationToken);
		var restored = restoredResult.IsSuccess ? restoredResult.Value : null;

		await Assert.That(restored).IsNotNull();
		await Assert.That(restored!.ReportSummary).IsNotNull();
		await Assert.That(restored.ReportSummaryScalar).IsNotNull();
		await Assert.That(restored.ReportSummary!.Value.ParserDetails.TotalLines).IsEqualTo(10);
		await Assert.That(restored.ReportSummary.Value.ParserDetails.FailedLines).IsEqualTo(5);
		await Assert.That(restored.ReportSummary.Value.Projects.Count()).IsEqualTo(2);
		await Assert.That(restored.ReportSummary.Value.AssetDetails.OperatingSystemDistribution).Count().IsEqualTo(3);
		await Assert.That(restored.ReportSummaryScalar!.ParserDetails.TotalLines).IsEqualTo(10);
		await Assert.That(restored.ReportSummaryScalar.ParserDetails.FailedLines).IsEqualTo(5);
		await Assert.That(restored.ReportSummaryScalar.Projects.Count()).IsEqualTo(2);

		var translatableQuery = await store.QueryAsync(
			a => a.ReportSummaryScalar!.ParserDetails.FailedLines > 0,
			cancellationToken: cancellationToken
		);

		await Assert.That(translatableQuery.Results).Count().IsEqualTo(1);
		await Assert.That(translatableQuery.Results[0].Id()).IsEqualTo(id);
		await Assert.That(translatableQuery.Results[0].ReportSummaryScalar).IsNotNull();
		await Assert.That(translatableQuery.Results[0].ReportSummaryScalar!.ParserDetails.FailedLines).IsEqualTo(5);
	}

	[Test]
	public async Task QueryAsync_GivenFilterOnReportSummaryScalarInnerValue_Translates(
		CancellationToken cancellationToken
	)
	{
		var store = fixture.CreateSnapshotStore<ReportUploadAggregate>();
		var id = Guid.NewGuid().ToString("D");
		var aggregate = CreateCompletedAggregate(id);

		await store.SaveAsync(aggregate, cancellationToken);

		var query = await store.QueryAsync(
			a => a.ReportSummaryScalar!.ParserDetails.FailedLines > 0,
			cancellationToken: cancellationToken
		);

		await Assert.That(query.Results).Count().IsEqualTo(1);
		await Assert.That(query.Results[0].Id()).IsEqualTo(id);
		await Assert.That(query.Results[0].ReportSummaryScalar).IsNotNull();
		await Assert.That(query.Results[0].ReportSummaryScalar!.ParserDetails.FailedLines).IsEqualTo(5);
	}

	[Test]
	public async Task QueryAsync_GivenFilterOnComplexValueInnerMember_AndMirrorProperty_Translate(
		CancellationToken cancellationToken
	)
	{
		var store = fixture.CreateSnapshotStore<ReportUploadAggregate>();
		var id = Guid.NewGuid().ToString("D");
		var aggregate = CreateCompletedAggregate(id);

		await store.SaveAsync(aggregate, cancellationToken);

		var canonicalQuery = await store.QueryAsync(
			a => a.ReportSummary!.Value.ParserDetails.FailedLines > 0,
			cancellationToken: cancellationToken
		);

		await Assert.That(canonicalQuery.Results).Count().IsEqualTo(1);

		var supportedQuery = await store.QueryAsync(
			a => a.ReportSummaryScalar!.ParserDetails.FailedLines > 0,
			cancellationToken: cancellationToken
		);

		await Assert.That(supportedQuery.Results).Count().IsEqualTo(1);
		await Assert.That(supportedQuery.Results[0].Id()).IsEqualTo(id);
	}
}
