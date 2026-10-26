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
}
