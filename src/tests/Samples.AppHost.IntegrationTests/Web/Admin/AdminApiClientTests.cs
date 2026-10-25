using System.Text.Json;
using Purview.EventSourcing.Admin.Client;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Fixtures;

namespace Purview.EventSourcing.Samples.Web.Admin;

/// <summary>
/// Exercises every Admin API endpoint through the generated <see cref="AdminApiClient"/> against the real
/// (SQL Server backed) sample store and asserts the returned data is correct.
/// </summary>
[NotInParallel("SamplesAppHost")]
[ClassDataSource<AppHostFixture>(Shared = SharedType.PerTestSession)]
public sealed class AdminAPIClientTests(AppHostFixture fixture)
{
	const string ExpectedAggregateType = "order";

	[Test]
	public async Task SearchAggregates_ReturnsSeededAggregateData(CancellationToken cancellationToken)
	{
		var (aggregateId, _) = await SeedOrderAsync(cancellationToken);
		var client = CreateClient();

		var result = await client.SearchAggregatesAsync(
			new AggregateSearchRequest { AggregateId = aggregateId, PageSize = 25 },
			cancellationToken
		);

		await Assert.That(result.Items.Count).IsEqualTo(1);
		var item = result.Items.First();
		await Assert.That(item.AggregateType).IsEqualTo(ExpectedAggregateType);
		await Assert.That(item.AggregateId).IsEqualTo(aggregateId);
		await Assert.That(item.CurrentVersion).IsGreaterThanOrEqualTo(3);
		await Assert.That(item.CreatedUtc).IsGreaterThanOrEqualTo(DateTimeOffset.UtcNow.AddMinutes(-5));
		await Assert.That(item.IsDeleted).IsFalse();
		await Assert.That(item.IsRestored).IsTrue();
	}

	[Test]
	public async Task GetAggregate_ReturnsSeededAggregateData(CancellationToken cancellationToken)
	{
		var (aggregateId, _) = await SeedOrderAsync(cancellationToken);
		var client = CreateClient();

		var result = await client.GetAggregateAsync(ExpectedAggregateType, aggregateId, cancellationToken);

		await Assert.That(result.AggregateType).IsEqualTo(ExpectedAggregateType);
		await Assert.That(result.AggregateId).IsEqualTo(aggregateId);
		await Assert.That(result.CurrentVersion).IsGreaterThanOrEqualTo(3);
		await Assert.That(result.CreatedUtc).IsGreaterThanOrEqualTo(DateTimeOffset.UtcNow.AddMinutes(-5));
		await Assert.That(result.IsDeleted).IsFalse();
		await Assert.That(result.IsRestored).IsTrue();
	}

	[Test]
	public async Task GetAggregateEventRange_ReturnsSeededEventData(CancellationToken cancellationToken)
	{
		var (aggregateId, customerId) = await SeedOrderAsync(cancellationToken);
		var client = CreateClient();

		var result = await client.GetAggregateEventRangeAsync(
			ExpectedAggregateType,
			aggregateId,
			pageSize: 25,
			cancellationToken: cancellationToken
		);

		await Assert.That(result).IsNotNull();
		await Assert.That(result.Items.Count).IsEqualTo(3);
		await Assert.That(result.TotalCount).IsEqualTo(3);
		await Assert.That(result.Page).IsEqualTo(1);

		var events = result.Items.ToList();
		var eventTypes = events.Select(e => e.Metadata.EventType).ToList();
		await Assert.That(eventTypes).Contains("order.order-created");
		await Assert.That(eventTypes).Contains("order.line-item-added");
		await Assert.That(eventTypes).Contains("order.shipping-address-set");

		for (var i = 0; i < events.Count; i++)
		{
			await Assert.That(events[i].Metadata.Version).IsEqualTo(i + 1);
			await Assert.That(events[i].Metadata.SchemaVersion).IsEqualTo(1);
			await Assert.That(events[i].AggregateType).IsEqualTo(ExpectedAggregateType);
			await Assert.That(events[i].AggregateId).IsEqualTo(aggregateId);
			await Assert
				.That(events[i].Metadata.TimestampUtc)
				.IsGreaterThanOrEqualTo(DateTimeOffset.UtcNow.AddMinutes(-5));
		}

		var payload = events[0].Payload;
		await Assert.That(payload).IsNotNull();
		var createdPayload = JsonSerializer.Serialize(payload!.AdditionalProperties);
		await Assert.That(createdPayload).Contains(customerId);
	}

	[Test]
	public async Task ProjectionAtVersion_ReturnsProjectedStateData(CancellationToken cancellationToken)
	{
		var (aggregateId, _) = await SeedOrderAsync(cancellationToken);
		var client = CreateClient();

		var result = await client.GetAggregateProjectionAtVersionAsync(
			ExpectedAggregateType,
			aggregateId,
			version: 3,
			cancellationToken: cancellationToken
		);

		await Assert.That(result).IsNotNull();
		await Assert.That(result.ProjectedVersion).IsEqualTo(3);
		await Assert.That(result.Provenance.AppliedCount).IsEqualTo(3);
		await Assert.That(result.Provenance.SkippedCount).IsEqualTo(0);
		await Assert.That(result.Provenance.AppliedVersions).Count().IsEqualTo(3);

		var stateJson = JsonSerializer.Serialize(result.State.AdditionalProperties);
		await Assert.That(stateJson).Contains("\"event_1\"");
		await Assert.That(stateJson).Contains("order.order-created");
		await Assert.That(stateJson).Contains("\"event_3\"");
		await Assert.That(stateJson).Contains("order.shipping-address-set");
	}

	[Test]
	public async Task ProjectionAtTime_ReturnsProjectedStateData(CancellationToken cancellationToken)
	{
		var (aggregateId, _) = await SeedOrderAsync(cancellationToken);
		var client = CreateClient();

		var result = await client.GetAggregateProjectionAtTimeAsync(
			ExpectedAggregateType,
			aggregateId,
			DateTimeOffset.UtcNow.AddMinutes(1),
			cancellationToken
		);

		await Assert.That(result).IsNotNull();
		await Assert.That(result.ProjectedVersion).IsEqualTo(3);
		await Assert.That(result.Provenance.AppliedCount).IsEqualTo(3);

		var stateJson = JsonSerializer.Serialize(result.State.AdditionalProperties);
		await Assert.That(stateJson).Contains("\"event_1\"");
		await Assert.That(stateJson).Contains("order.order-created");
	}

	[Test]
	public async Task ExportEvents_ReturnsAllEventsAsNdjson(CancellationToken cancellationToken)
	{
		var (aggregateId, _) = await SeedOrderAsync(cancellationToken);
		var client = CreateClient();

		using var response = await client.ExportAggregateEventsAsync(
			ExpectedAggregateType,
			aggregateId,
			cancellationToken: cancellationToken
		);
		using StreamReader reader = new(response.Stream);
		var text = await reader.ReadToEndAsync(cancellationToken);

		var lines = text.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

		await Assert.That(lines.Count).IsEqualTo(3);
		foreach (var line in lines)
		{
			using var doc = JsonDocument.Parse(line);
			await Assert.That(doc.RootElement.GetProperty("aggregateId").GetString()).IsEqualTo(aggregateId);
		}

		using var firstDoc = JsonDocument.Parse(lines[0]);
		await Assert.That(firstDoc.RootElement.GetProperty("metadata").GetProperty("version").GetInt64()).IsEqualTo(1);
		await Assert
			.That(firstDoc.RootElement.GetProperty("metadata").GetProperty("eventType").GetString())
			.IsEqualTo("order.order-created");
	}

	[Test]
	public async Task SearchAggregates_InvalidRequest_ThrowsValidationProblem(CancellationToken cancellationToken)
	{
		var client = CreateClient();

		var exception = await Assert.ThrowsAsync<AdminApiException<HttpValidationProblemDetails>>(() =>
			client.SearchAggregatesAsync(new AggregateSearchRequest { Page = 0 }, cancellationToken)
		);

		await Assert.That(exception?.StatusCode).IsEqualTo(400);
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Reliability",
		"CA2000:Dispose objects before losing scope",
		Justification = "The fixture-scoped client is disposed with the test session."
	)]
	AdminApiClient CreateClient() => new(string.Empty, fixture.CreateWebClient());

	async Task<(string AggregateId, string CustomerId)> SeedOrderAsync(CancellationToken cancellationToken)
	{
		var store = fixture.QueryableEventStore();
		var customerId = $"client-customer-{Guid.NewGuid():N}";

		var order = await store.CreateAsync<OrderAggregate>(cancellationToken: cancellationToken);
		order
			.CreateOrder(customerId)
			.AddLineItem("SKU-CLIENT-001", "Client Test Product", 1, 9.99m)
			.SetShippingAddress("1 Client Test Way");

		var result = await store.SaveAsync(order, cancellationToken);
		return (result.Value.Aggregate.Id(), customerId);
	}
}
