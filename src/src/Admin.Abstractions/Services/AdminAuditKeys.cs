namespace Purview.EventSourcing.Admin.Services;

/// <summary>
/// The dependency-injection keys used to wire the audit logger.
/// </summary>
/// <remarks>
/// The logger that actually records entries is registered under <see cref="Sink"/>, and the
/// unkeyed <see cref="IAdminAuditLogger"/> is a decorator that stamps the tenant and correlation
/// identifier onto every entry before passing it on.
/// <para>
/// Keyed rather than decorating whatever happens to be registered, because that depends on the
/// order an application calls the registration helpers in: register the Admin API before the
/// durable logger and the decoration is missed, register it after and there is nothing to decorate
/// yet. A key removes the ordering question entirely.
/// </para>
/// </remarks>
public static class AdminAuditKeys
{
	/// <summary>The key the recording logger is registered under.</summary>
	public const string Sink = "purview-admin-audit-sink";
}
