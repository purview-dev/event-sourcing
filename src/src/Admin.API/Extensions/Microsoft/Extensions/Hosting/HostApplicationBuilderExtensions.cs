using System.ComponentModel;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Purview.EventSourcing.Admin.API.Auditing;
using Purview.EventSourcing.Admin.API.Contracts;
using ZodSharp.AspNetCore;

namespace Microsoft.Extensions.Hosting;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class HostApplicationBuilderExtensions
{
	const string OpenAPIDocumentName = "admin";

	extension<TBuilder>(TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		public string OpenAPIDocumentName => OpenAPIDocumentName;

		/// <summary>
		/// Adds the Admin API option bindings and validation hooks to the service collection.
		/// </summary>
		/// <param name = "configure">Optional in-memory configuration delegate.</param>
		/// <returns>The configured service collection.</returns>
		public TBuilder AddPurviewEventSourcingAdminAPI(Action<AdminPortalOptions>? configure = null)
		{
			builder
				.Services.AddOptions<AdminPortalOptions>()
				.Configure(options => configure?.Invoke(options))
				.ValidateOnStart();

			builder.Services.AddZodSchemaOptionsValidator<AdminPortalOptions>();

			// Register the ZodSharp schema factory and auto-discover the generated validators for the Admin API
			// request contracts and options so endpoint filters and option validation can resolve them by type.
			builder.Services.AddZodSharp(options => options.ScanAssemblies.Add(typeof(EventRangeRequest).Assembly));

			// Stamps the tenant and correlation identifier onto every audit entry. Added here, after
			// the application has registered whichever logger it wants, so a durable provider logger
			// is decorated just as the in-memory default is.
			builder.Services.AddAdminAuditContextStamping();

			return builder;
		}

		/// <summary>
		/// Adds the Admin API OpenAPI document (<c>admin</c>), restricted to the Admin API endpoints and
		/// annotated with a bearer security scheme that applies to every operation.
		/// </summary>
		/// <param name = "configure">Optional additional <see cref = "OpenApiOptions"/> configuration.</param>
		/// <returns>The configured service collection.</returns>
		/// <remarks>
		/// <para>
		/// The generated document is available at <c>/openapi/admin.json</c> when the host also calls
		/// <c>MapOpenApi()</c>. All Admin API endpoints require authorization, so the document declares a global
		/// HTTP bearer requirement; generated clients can attach a bearer token to authenticate.
		/// </para>
		/// </remarks>
		public TBuilder AddPurviewEventSourcingAdminOpenAPI(Action<OpenApiOptions>? configure = null)
		{
			builder.Services.AddOpenApi(
				OpenAPIDocumentName,
				options =>
				{
					options.AddDocumentTransformer(
						static (document, context, _) =>
						{
							var adminOptions = context
								.ApplicationServices.GetRequiredService<IOptions<AdminPortalOptions>>()
								.Value;
							var prefix = adminOptions.RoutePrefix.TrimEnd('/');
							if (document.Paths is not null)
							{
								foreach (var path in document.Paths.Keys.ToList())
								{
									if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
										document.Paths.Remove(path);
								}
							}

							// The document is generated from an HTTP request, so ASP.NET Core adds the ephemeral request
							// origin as a server entry. Clear it so the committed spec (and generated clients) are
							// host-agnostic and base URLs can be supplied by consumers.
							document.Servers = [];
							// ASP.NET Core describes numeric properties as an ["integer"/"string"] union with a coercion
							// pattern (so query values arrive as strings). That representation confuses typed-client
							// generators, so the types are collapsed back to plain numeric/boolean schemas here.
							if (document.Components?.Schemas is not null)
							{
								foreach (var schema in document.Components.Schemas.Values)
									NormalizeSchemaTypes(schema);
							}

							OpenApiSecurityScheme securityScheme = new()
							{
								Type = SecuritySchemeType.Http,
								Scheme = "bearer",
								BearerFormat = "JWT",
								Description = "Bearer token for the Admin portal API.",
							};
							var components = document.Components ?? new OpenApiComponents();
							document.Components = components;
							components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
							components.SecuritySchemes["Bearer"] = securityScheme;
							OpenApiSecuritySchemeReference reference = new("Bearer", document, null);
							document.Security = [new OpenApiSecurityRequirement { [reference] = [] }];
							return Task.CompletedTask;
						}
					);
					options.AddOperationTransformer(
						static (operation, _, _) =>
						{
							// The event export streams raw JSON Lines, so it must be modelled as binary (format: binary)
							// rather than a JSON-encoded byte array (format: byte) for typed-client generators to read the
							// raw stream.
							if (operation.OperationId == "ExportAggregateEvents" && operation.Responses is not null)
							{
								if (
									operation.Responses.TryGetValue("200", out var response)
									&& response.Content is not null
								)
								{
									// IOpenApiMediaType exposes Schema read-only; the concrete media type is what
									// carries the setter.
									foreach (var content in response.Content.Values)
									{
										if (content is not OpenApiMediaType mediaType)
											continue;

										mediaType.Schema = new OpenApiSchema
										{
											Type = JsonSchemaType.String,
											Format = "binary",
										};
									}
								}
							}

							return Task.CompletedTask;
						}
					);
					configure?.Invoke(options);
				}
			);
			return builder;
		}
	}

	static void NormalizeSchemaTypes(IOpenApiSchema schema)
	{
		if (schema.Properties is not null)
		{
			foreach (var property in schema.Properties.Values)
				NormalizeSchemaTypes(property);
		}

		if (schema is OpenApiSchema concrete && concrete.Type is JsonSchemaType type)
		{
			if ((type & JsonSchemaType.Integer) != 0)
				concrete.Type = JsonSchemaType.Integer;
			else if ((type & JsonSchemaType.Number) != 0)
				concrete.Type = JsonSchemaType.Number;
			else if ((type & JsonSchemaType.Boolean) != 0)
				concrete.Type = JsonSchemaType.Boolean;
			if (concrete.Type is JsonSchemaType.Integer or JsonSchemaType.Number or JsonSchemaType.Boolean)
				concrete.Pattern = null;
		}

		if (schema.Items is not null)
			NormalizeSchemaTypes(schema.Items);
		if (schema.OneOf is not null)
		{
			foreach (var nested in schema.OneOf)
				NormalizeSchemaTypes(nested);
		}

		if (schema.AnyOf is not null)
		{
			foreach (var nested in schema.AnyOf)
				NormalizeSchemaTypes(nested);
		}
	}
}
