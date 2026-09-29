using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Purview.Aspire.ResourceKit;
using ZodSharp;

namespace Purview.EventSourcing.Samples.AppHost.AppModel.Resources;

[ResourceDefinition<ResourceGroup>(Platform.WebApp)]
sealed partial class WebAppKit
{
	ImmutableDictionary<string, IResourceBuilder<ProjectResource>> _webAppVariants = [];

	protected override IResourceBuilder<ResourceGroup> BuildResource(IDistributedApplicationBuilder builder)
	{
		var resourceGroup = builder.AddResource(new ResourceGroup(Name)).WithIconName("AddStarburstColor");
		var variants = ImmutableDictionary.CreateBuilder<string, IResourceBuilder<ProjectResource>>();
		foreach (var variant in Options.Variants)
		{
			var webApp = builder
				.AddProject<Projects.Samples_Web>(
					variant.Value.ResourceName,
					c =>
					{
						//
					}
				)
				.WithHttpEndpoint(name: "http", isProxied: false)
				.WithHttpsEndpoint(name: "https", isProxied: false);

			webApp.WithParentRelationship(resourceGroup);

			variants.Add(variant.Key, webApp);
		}

		_webAppVariants = variants.ToImmutable();

		return resourceGroup;
	}

	protected override void ConfigureResource()
	{
		foreach (var variant in Options.Variants)
		{
			var webApp = _webAppVariants[variant.Key];
			ConfigureVariant(webApp, variant.Key, Options.Variants[variant.Key]);
		}

		base.ConfigureResource();
	}

	[ZodSchema]
	public sealed partial class WebAppKitOptions
	{
		[Required]
		public Dictionary<string, VariantConfiguration> Variants { get; set; } = [];

		[Required(AllowEmptyStrings = false)]
		public string AdminSitePath { get; set; } = string.Empty;

		[Required(AllowEmptyStrings = false)]
		public string AdminAPIPath { get; set; } = string.Empty;

		[Required(AllowEmptyStrings = false)]
		public string DataIsolationWarning { get; set; } = string.Empty;
	}
}
