using System.Security.Claims;

namespace Purview.EventSourcing;

/// <summary>
/// Supplies the <see cref="ClaimsPrincipal"/> a save records as the originating user in
/// <see cref="EventMetadata.UserId"/>.
/// </summary>
/// <remarks>
/// Resolve this from dependency injection rather than reading an ambient principal: ASP.NET Core does not
/// populate <see cref="Thread.CurrentPrincipal"/>, so <see cref="ClaimsPrincipal.Current"/> is
/// <see langword="null"/> in a web host unless the application sets
/// <see cref="ClaimsPrincipal.ClaimsPrincipalSelector"/> itself. Register an implementation backed by the
/// host's own notion of the current user - <c>IHttpContextAccessor</c> in a web application - to record a
/// user against saved events.
/// </remarks>
public interface IEventStorePrincipalAccessor
{
	/// <summary>
	/// Gets the principal for the operation in progress, or <see langword="null"/> when there is none.
	/// </summary>
	ClaimsPrincipal? GetPrincipal();
}
