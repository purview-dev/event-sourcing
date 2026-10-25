using System.Globalization;
using Purview.EventSourcing.Samples.Domain;
using Purview.EventSourcing.Samples.Fixtures;

namespace Purview.EventSourcing.Samples.Web.Pages;

[NotInParallel("SamplesAppHost")]
[ClassDataSource<AppHostFixture>(Shared = SharedType.PerTestSession)]
public sealed class AuditPageTests(AppHostFixture fixture)
{
	readonly HttpClient _client = fixture.CreateWebClient();

	[Test]
	public async Task GetAuditPage_GivenDateRangeOnly_ShowsRecentEvents(CancellationToken cancellationToken)
	{
		await CreateOrderAsync(cancellationToken);
		var fromUtc = DateTimeOffset.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

		var response = await _client.GetAsync(
			$"/BackOffice/Audit/Index?aggregateType=order&fromUtc={fromUtc}",
			cancellationToken
		);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
		await Assert.That(content).Contains("Recent Events");
		await Assert.That(content).DoesNotContain("No recent events matched the current filters.");
	}

	[Test]
	public async Task GetAuditPage_GivenAggregateId_ShowsAggregateHistoryEvents(CancellationToken cancellationToken)
	{
		var aggregateId = await CreateOrderAsync(cancellationToken);
		var response = await _client.GetAsync(
			$"/BackOffice/Audit/Index?aggregateType=order&aggregateId={aggregateId}",
			cancellationToken
		);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
		await Assert.That(content).Contains("Events");
		await Assert.That(content).DoesNotContain("No events matched the current filters.");
	}

	[Test]
	public async Task GetAuditPage_GivenNoFilters_ShowsRecentEvents(CancellationToken cancellationToken)
	{
		await CreateOrderAsync(cancellationToken);
		var response = await _client.GetAsync("/BackOffice/Audit/Index", cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);

		await Assert.That(response.IsSuccessStatusCode).IsTrue();
		await Assert.That(content).Contains("Recent Events");
		await Assert.That(content).DoesNotContain("No recent events matched the current filters.");
	}

	async Task<string> CreateOrderAsync(CancellationToken cancellationToken)
	{
		var store = fixture.QueryableEventStore();
		var customer = await store.CreateAsync<CustomerAggregate>(cancellationToken: cancellationToken);
		customer.RegisterCustomer($"audit-{Guid.NewGuid():N}", $"audit-{Guid.NewGuid():N}@example.com");
		var customerResult = await store.SaveAsync(customer, cancellationToken);

		var order = await store.CreateAsync<OrderAggregate>(cancellationToken: cancellationToken);
		order
			.CreateOrder(customerResult.Value.Aggregate.Id())
			.AddLineItem("SKU-AUDIT-001", "Audit Product", 1, 9.99m)
			.SetShippingAddress("1 Audit Way");
		var orderResult = await store.SaveAsync(order, cancellationToken);

		return orderResult.Value.Aggregate.Id();
	}

	//sealed class AuditWebAppFactory : WebApplicationFactory<Program>
	//{
	//	protected override void ConfigureWebHost(IWebHostBuilder builder) =>
	//		builder.ConfigureTestServices(services =>
	//		{
	//			services.RemoveAll<IAggregateAuditService>();
	//			services.RemoveAll<ISeedDataService>();
	//			services.AddSingleton<IAggregateAuditService, FakeAggregateAuditService>();
	//			services.AddSingleton<ISeedDataService, NoOpSeedDataService>();
	//		});
	//}

	//sealed class NoOpSeedDataService : ISeedDataService
	//{
	//	public Task SeedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	//}

	//sealed class FakeAggregateAuditService : IAggregateAuditService
	//{
	//	public Task<ContinuationResponse<AggregateEventHistoryItem>> GetHistoryAsync(
	//		string aggregateType,
	//		string aggregateId,
	//		AggregateEventHistoryRequest request,
	//		CancellationToken cancellationToken
	//	) =>
	//		Task.FromResult(
	//			new ContinuationResponse<AggregateEventHistoryItem>
	//			{
	//				RequestedCount = request.MaxRecords,
	//				Results =
	//				[
	//					new AggregateEventHistoryItem
	//					{
	//						AggregateType = "OrderAggregate",
	//						AggregateId = aggregateId,
	//						AggregateVersion = 1,
	//						EventType = "AggregateIdEvent",
	//						EventClrType = "AggregateIdEvent",
	//						Payload = "{}",
	//						When = DateTimeOffset.UtcNow,
	//					},
	//				],
	//			}
	//		);

	//	public Task<IReadOnlyList<AggregateEventHistoryItem>> GetLatestHistoryAsync(
	//		string aggregateType,
	//		AggregateEventHistoryRequest request,
	//		CancellationToken cancellationToken
	//	) =>
	//		Task.FromResult<IReadOnlyList<AggregateEventHistoryItem>>([
	//			new AggregateEventHistoryItem
	//			{
	//				AggregateType = "OrderAggregate",
	//				AggregateId = "range-agg",
	//				AggregateVersion = 2,
	//				EventType = "DateRangeOnlyEvent",
	//				EventClrType = "DateRangeOnlyEvent",
	//				Payload = "{}",
	//				When = DateTimeOffset.UtcNow,
	//			},
	//		]);
	//}
}
