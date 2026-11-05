using System.Security.Claims;

namespace Purview.EventSourcing;

/// <summary>
/// The default <see cref="IEventStorePrincipalAccessor"/>, reading <see cref="ClaimsPrincipal.Current"/>.
/// </summary>
/// <remarks>
/// Only useful where the application populates the ambient principal - by setting
/// <see cref="ClaimsPrincipal.ClaimsPrincipalSelector"/>, or on a host that flows
/// <see cref="Thread.CurrentPrincipal"/>. ASP.NET Core does neither, so a web application should register
/// its own accessor; see <see cref="IEventStorePrincipalAccessor"/>.
/// </remarks>
public sealed class AmbientEventStorePrincipalAccessor : IEventStorePrincipalAccessor
{
	/// <inheritdoc/>
	public ClaimsPrincipal? GetPrincipal() => ClaimsPrincipal.Current;
}
