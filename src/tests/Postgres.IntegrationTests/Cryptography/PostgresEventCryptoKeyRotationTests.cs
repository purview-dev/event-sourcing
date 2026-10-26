using Microsoft.Extensions.Options;
using Purview.EventSourcing.Cryptography;
using Purview.EventSourcing.Fixtures.Postgres;
using Purview.EventSourcing.Postgres.Events;
using Purview.EventSourcing.Tenancy;

namespace Purview.EventSourcing.Postgres.Cryptography;

/// <summary>
/// Key rotation against real PostgreSQL.
/// </summary>
/// <remarks>
/// Rotation limits the blast radius of a future key compromise. It does not re-encrypt history, so
/// the property that matters is that everything written under an earlier generation stays readable -
/// a rotation that quietly broke old events would be far worse than not rotating at all.
/// </remarks>
[ClassDataSource<PostgresEventStoreFixture>(Shared = SharedType.PerTestSession)]
public sealed class PostgresEventCryptoKeyRotationTests(PostgresEventStoreFixture fixture)
{
	[Test]
	public async Task RotateAsync_ProducesANewCurrentGeneration(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		var before = await AsTenantAsync("acme", () => store.GetCurrentAsync(subject, cancellationToken));
		var rotated = await AsTenantAsync("acme", () => store.RotateAsync(subject, cancellationToken));
		var after = await AsTenantAsync("acme", () => store.GetCurrentAsync(subject, cancellationToken));

		await Assert.That(rotated).IsNotEqualTo(before.KeyId);
		await Assert.That(after.KeyId).IsEqualTo(rotated);
		await Assert.That(Convert.ToBase64String(after.Key)).IsNotEqualTo(Convert.ToBase64String(before.Key));
	}

	[Test]
	public async Task TryGetAsync_AfterRotation_StillReturnsTheEarlierGeneration(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		var before = await AsTenantAsync("acme", () => store.GetCurrentAsync(subject, cancellationToken));
		await AsTenantAsync("acme", () => store.RotateAsync(subject, cancellationToken));

		var earlier = await AsTenantAsync("acme", () => store.TryGetAsync(subject, before.KeyId, cancellationToken));

		await Assert.That(Convert.ToBase64String(earlier!)).IsEqualTo(Convert.ToBase64String(before.Key));
	}

	// The whole point of retaining generations: a payload written before a rotation must still be
	// readable afterwards.
	[Test]
	public async Task UnprotectAsync_ReadsAPayloadWrittenBeforeRotation(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		AesGcmEventPayloadProtector protector = new(store);
		var subject = $"{Guid.NewGuid():N}";

		var before = await AsTenantAsync(
			"acme",
			() => protector.ProtectAsync("""{"value":"written-before"}""", subject, cancellationToken)
		);

		await AsTenantAsync("acme", () => store.RotateAsync(subject, cancellationToken));

		var after = await AsTenantAsync(
			"acme",
			() => protector.ProtectAsync("""{"value":"written-after"}""", subject, cancellationToken)
		);

		var readBefore = await AsTenantAsync("acme", () => protector.UnprotectAsync(before, cancellationToken));
		var readAfter = await AsTenantAsync("acme", () => protector.UnprotectAsync(after, cancellationToken));

		await Assert.That(readBefore).IsEqualTo("""{"value":"written-before"}""");
		await Assert.That(readAfter).IsEqualTo("""{"value":"written-after"}""");
	}

	// Erasure must remove every generation. One left behind would mean the subject's older events
	// were still readable, which is not an erasure.
	[Test]
	public async Task ShredAsync_RemovesEveryGeneration(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		var first = await AsTenantAsync("acme", () => store.GetCurrentAsync(subject, cancellationToken));
		var second = await AsTenantAsync("acme", () => store.RotateAsync(subject, cancellationToken));

		await AsTenantAsync("acme", () => store.ShredAsync(subject, cancellationToken));

		var firstAfter = await AsTenantAsync("acme", () => store.TryGetAsync(subject, first.KeyId, cancellationToken));
		var secondAfter = await AsTenantAsync("acme", () => store.TryGetAsync(subject, second, cancellationToken));

		await Assert.That(firstAfter).IsNull();
		await Assert.That(secondAfter).IsNull();
	}

	// Rotating one tenant's subject must not touch another tenant's subject of the same name.
	[Test]
	public async Task RotateAsync_IsScopedToTheTenant(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		var globexBefore = await AsTenantAsync("globex", () => store.GetCurrentAsync(subject, cancellationToken));
		await AsTenantAsync("acme", () => store.RotateAsync(subject, cancellationToken));
		var globexAfter = await AsTenantAsync("globex", () => store.GetCurrentAsync(subject, cancellationToken));

		await Assert.That(globexAfter.KeyId).IsEqualTo(globexBefore.KeyId);
		await Assert.That(Convert.ToBase64String(globexAfter.Key)).IsEqualTo(Convert.ToBase64String(globexBefore.Key));
	}

	[Test]
	public async Task TryGetAsync_GivenAnUnknownGeneration_ReturnsNull(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		await AsTenantAsync("acme", () => store.GetCurrentAsync(subject, cancellationToken));

		var missing = await AsTenantAsync("acme", () => store.TryGetAsync(subject, "99", cancellationToken));
		var nonsense = await AsTenantAsync(
			"acme",
			() => store.TryGetAsync(subject, "not-a-generation", cancellationToken)
		);

		await Assert.That(missing).IsNull();
		await Assert.That(nonsense).IsNull();
	}

	// Concurrent rotations collide on the generation index; the loser must retry rather than fail.
	[Test]
	public async Task RotateAsync_UnderContention_ProducesDistinctGenerations(CancellationToken cancellationToken)
	{
		using var store = CreateStore();
		var subject = $"{Guid.NewGuid():N}";

		await AsTenantAsync("acme", () => store.GetCurrentAsync(subject, cancellationToken));

		var generations = await AsTenantAsync(
			"acme",
			async () =>
				await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.RotateAsync(subject, cancellationToken)))
		);

		await Assert.That(generations.Distinct().Count()).IsEqualTo(generations.Length);
	}

	static async Task<T> AsTenantAsync<T>(string tenant, Func<Task<T>> operation)
	{
		using (AmbientCurrentTenant.BeginScope(TenantScope.For(tenant)))
			return await operation();
	}

	PostgresEventCryptoKeyStore CreateStore() =>
		new(
			Options.Create(
				new PostgresEventCryptoKeyStoreOptions
				{
					ConnectionString = fixture.ConnectionString,
					TableName = $"CryptoKeys_{Guid.NewGuid():N}",
				}
			),
			Options.Create(new PostgresEventStoreOptions { ConnectionString = fixture.ConnectionString }),
			new AmbientCurrentTenant()
		);
}
