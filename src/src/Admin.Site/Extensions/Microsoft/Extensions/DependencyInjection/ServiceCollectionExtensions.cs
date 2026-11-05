using Microsoft.AspNetCore.Http;
using Purview.EventSourcing.Admin.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Adds the admin portal Razor Pages to the MVC application and registers the generated Admin API client used
	/// by the pages.
	/// </summary>
	/// <param name = "services">The service collection to configure.</param>
	/// <param name = "enableRazorRuntimeCompilation">
	/// When <see langword="true"/>, enables Razor runtime compilation so page markup can be edited without rebuilding.
	/// </param>
	/// <param name = "configureClient">Optional <see cref = "AdminClientOptions"/> configuration.</param>
	/// <returns>The configured MVC builder for chaining.</returns>
	/// <exception cref = "ArgumentNullException"><paramref name = "services"/> is <see langword="null"/>.</exception>
	public static IMvcBuilder AddPurviewEventSourcingAdminSite(
		this IServiceCollection services,
		bool enableRazorRuntimeCompilation = false,
		Action<AdminClientOptions>? configureClient = null
	)
	{
		ArgumentNullException.ThrowIfNull(services);

		var mvcBuilder = services.AddRazorPages();
		if (enableRazorRuntimeCompilation)
		{
			// ASP.NET Core 11 deprecates runtime compilation (ASPDEPR003). This stays opt-in and
			// development-only; removing the parameter is a public API change and is tracked separately.
#pragma warning disable ASPDEPR003
			mvcBuilder.AddRazorRuntimeCompilation();
#pragma warning restore ASPDEPR003
		}

		services.AddHttpContextAccessor();
		services.AddTransient<SameOriginResolverHandler>();
		services.AddAdminAPIClient(
			configureClient,
			clientBuilder => clientBuilder.AddHttpMessageHandler<SameOriginResolverHandler>()
		);

		return mvcBuilder;
	}

	sealed class SameOriginResolverHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
	{
		const string PlaceholderHost = "admin.invalid";

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken
		)
		{
			if (
				request.RequestUri is { } uri
				&& (!uri.IsAbsoluteUri || uri.Host.Equals(PlaceholderHost, StringComparison.OrdinalIgnoreCase))
			)
			{
				var httpContext =
					httpContextAccessor.HttpContext
					?? throw new InvalidOperationException(
						"No active HTTP request is available to resolve the Admin API origin. Configure AdminClientOptions.BaseUrl instead."
					);

				Uri origin = new($"{httpContext.Request.Scheme}://{httpContext.Request.Host}");
				var pathAndQuery = uri.IsAbsoluteUri ? uri.PathAndQuery : "/" + uri.OriginalString;
				request.RequestUri = new Uri(origin, pathAndQuery);
			}

			return base.SendAsync(request, cancellationToken);
		}
	}
}
