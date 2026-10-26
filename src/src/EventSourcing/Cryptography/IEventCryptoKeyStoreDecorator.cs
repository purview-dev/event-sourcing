namespace Purview.EventSourcing.Cryptography;

/// <summary>
/// An <see cref="IEventCryptoKeyStore"/> that wraps another.
/// </summary>
/// <remarks>
/// Lets a check that cares about the real store - such as the one refusing a key store that cannot
/// survive a restart - see through a decorator rather than inspecting the outermost wrapper and
/// drawing the wrong conclusion. Implement it on any decorator, or the guard silently stops
/// protecting whatever is underneath.
/// </remarks>
public interface IEventCryptoKeyStoreDecorator
{
	/// <summary>The store being wrapped.</summary>
	IEventCryptoKeyStore Inner { get; }
}
