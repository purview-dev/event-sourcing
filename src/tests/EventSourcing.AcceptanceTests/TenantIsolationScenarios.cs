using BDTest;
using Purview.EventSourcing.Steps;

namespace Purview.EventSourcing;

/// <summary>
/// The tenant isolation guarantee, stated as scenarios.
/// </summary>
/// <remarks>
/// These read as the promise the framework makes, rather than as a test of how it is implemented. Each
/// one corresponds to a real defect found while the unit and integration suites were green.
/// </remarks>
public sealed class TenantIsolationScenarios : TUnitBDTestBase<EventStoreScenarioContext>
{
	EventStoreSteps Steps => field ??= new(Context);

	EventStoreAssertions Expect => field ??= new(Context);

	[Test]
	public async Task OneTenantCannotReadAnotherTenantsAggregate() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 5))
			.When(() => Steps.TheTenantReadsTheAggregate("globex"))
			.Then(() => Expect.TheAggregateIsReportedAsAbsent())
			.WithScenarioText("One tenant cannot read another tenant's aggregate");

	[Test]
	public async Task TheOwningTenantCanReadItsOwnAggregate() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 5))
			.When(() => Steps.TheTenantReadsTheAggregate("acme"))
			.Then(() => Expect.TheAggregateIsReturned())
			.And(() => Expect.TheValueIs(5))
			.WithScenarioText("The owning tenant can read its own aggregate");

	[Test]
	public async Task InfrastructureCanReadAcrossEveryTenant() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 5))
			.When(() => Steps.InfrastructureReadsTheAggregate())
			.Then(() => Expect.TheAggregateIsReturned())
			.WithScenarioText("Infrastructure can read across every tenant");

	// The cache is consulted before the tenant-filtered storage lookup, so priming it as the owner is
	// what exposes an unqualified cache key.
	[Test]
	public async Task ACachedAggregateIsNotServedToAnotherTenant() =>
		await Given(() => Steps.AnAggregateExistsForTenant("acme", 5))
			.And(() => Steps.TheTenantReadsTheAggregateTwice("acme"))
			.When(() => Steps.TheTenantReadsTheAggregate("globex"))
			.Then(() => Expect.TheAggregateIsReportedAsAbsent())
			.WithScenarioText("A cached aggregate is not served to another tenant");

	// Fail-closed: a write path that missed the tenant stamp produces absent data for its own tenant,
	// rather than data readable by all of them.
	[Test]
	public async Task DataBelongingToNoTenantIsNotVisibleToATenant() =>
		await Given(() => Steps.AnUnownedAggregateExists())
			.When(() => Steps.TheTenantReadsTheAggregate("acme"))
			.Then(() => Expect.TheAggregateIsReportedAsAbsent())
			.WithScenarioText("Data belonging to no tenant is not visible to a tenant");
}
