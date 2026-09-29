using System.Diagnostics.CodeAnalysis;
using Purview.EventSourcing.Fixtures.Postgres;
using Purview.EventSourcing.Samples.Domain.ReportUpload;
using Purview.EventSourcing.Samples.ValueObjects;

namespace Purview.EventSourcing.Postgres.Snapshots;

[ClassDataSource<PostgresSnapshotEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class ReportUploadAggregateSnapshotEventStoreTests(PostgresSnapshotEventStoreFixture fixture)
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

		var restored = await store.GetAsync(id, cancellationToken: cancellationToken);

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

		await Assert.That(translatableQuery.Results.Length).IsEqualTo(1);
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

		await Assert.That(query.Results.Length).IsEqualTo(1);
		await Assert.That(query.Results[0].Id()).IsEqualTo(id);
		await Assert.That(query.Results[0].ReportSummaryScalar).IsNotNull();
		await Assert.That(query.Results[0].ReportSummaryScalar!.ParserDetails.FailedLines).IsEqualTo(5);
	}

	[Test]
	[SuppressMessage(
		"Maintainability",
		"CA1506",
		Justification = "Provider integration scenario intentionally exercises both persisted query paths."
	)]
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

		await Assert.That(canonicalQuery.Results.Length).IsEqualTo(1);

		var supportedQuery = await store.QueryAsync(
			a => a.ReportSummaryScalar!.ParserDetails.FailedLines > 0,
			cancellationToken: cancellationToken
		);

		await Assert.That(supportedQuery.Results.Length).IsEqualTo(1);
		await Assert.That(supportedQuery.Results[0].Id()).IsEqualTo(id);
	}
}
