using System.Diagnostics.CodeAnalysis;

namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// Marks an <see cref="IEventCryptoKeyStore"/> whose keys do not survive a restart.
/// </summary>
/// <remarks>
/// <para>
/// Losing a crypto-shredding key is indistinguishable from erasing it deliberately: every payload
/// encrypted under it becomes permanently unreadable, and nothing reports that it happened. A store
/// marked with this is therefore refused at start-up outside Development unless
/// <see cref="EventCryptoOptions.AllowNonDurableKeyStore"/> is set.
/// </para>
/// <para>
/// Implement it on any test double or development store so the guard recognises it. The check is on
/// this marker rather than on a concrete type, so a store the framework has never heard of is still
/// protected by it.
/// </para>
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1040:Avoid empty interfaces",
	Justification = "A marker is the point: the start-up guard tests for a capability a store declares about itself, rather than for a concrete type it has to know in advance."
)]
public interface INonDurableEventCryptoKeyStore;
