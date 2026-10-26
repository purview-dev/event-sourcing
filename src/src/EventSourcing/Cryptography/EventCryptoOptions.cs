namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// Configuration for crypto-shredding.
/// </summary>
public sealed class EventCryptoOptions
{
	/// <summary>The configuration section name used to bind these options.</summary>
	public const string Section = "EventStore:Cryptography";

	/// <summary>
	/// Permits an <see cref="INonDurableEventCryptoKeyStore"/> outside the Development environment.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Off by default, and deliberately awkward to turn on. A non-durable key store loses every key
	/// on restart, which makes every protected event permanently unreadable - the same outcome as
	/// erasing every data subject at once, with no error and no way back.
	/// </para>
	/// <para>
	/// Legitimate uses are a demo, an ephemeral test host, or a deployment that has accepted that
	/// consequence in writing. If you are reaching for this to make a start-up failure go away,
	/// register a durable key store instead.
	/// </para>
	/// </remarks>
	public bool AllowNonDurableKeyStore { get; set; }

	/// <summary>
	/// Caches keys in process memory, so a protect or unprotect is not a round trip every time.
	/// </summary>
	/// <remarks>
	/// On by default. Against a database the saving is modest; against Azure Key Vault, where reads
	/// are throttled and billed, an uncached store is impractical at volume. Turn it off only where
	/// an erasure must take effect immediately everywhere, accepting the cost - see
	/// <see cref="KeyCacheDuration"/>.
	/// </remarks>
	public bool CacheKeys { get; set; } = true;

	/// <summary>
	/// How long a key is held in the process-local cache.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a compliance parameter, not a tuning one. The cache is per process, so another
	/// instance holding a key keeps serving it until its own entry expires: this value is the upper
	/// bound on how long an erasure takes to take effect across a cluster.
	/// </para>
	/// <para>
	/// Longer means fewer round trips to the key store - which matters most against a vault, where
	/// reads are throttled - and a longer window in which a shredded subject is still readable
	/// somewhere.
	/// </para>
	/// </remarks>
	public TimeSpan KeyCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

	/// <summary>
	/// The cache's size limit, measured in bytes of key material.
	/// </summary>
	/// <remarks>
	/// Bounded so a large subject population cannot exhaust memory. At the default, roughly thirty
	/// thousand 32-byte keys are held before the cache starts evicting.
	/// </remarks>
	public long KeyCacheSizeLimit { get; set; } = 1024 * 1024;
}
