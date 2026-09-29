using System.ComponentModel.DataAnnotations;
using ZodSharp;
using ZodSharp.Schemas;

namespace Purview.EventSourcing.Admin.API;

/// <summary>
/// Configures the Admin portal endpoint, paging and projection limits.
/// </summary>
[ZodSchema]
public sealed class AdminPortalOptions
{
	/// <summary>
	/// The configuration section name used to bind these options.
	/// </summary>
	public const string Section = "AdminPortal";

	/// <summary>
	/// Gets or sets whether the Admin portal is enabled.
	/// </summary>
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// Gets or sets the route prefix used by the Admin portal.
	/// </summary>
	[Required(AllowEmptyStrings = false)]
	[StringLength(128)]
	[RegularExpression("^/", ErrorMessage = "{0} must start with '/'.")]
	public string RoutePrefix { get; set; } = "/admin/api";

	/// <summary>
	/// Gets or sets the feature toggle group.
	/// </summary>
	[Required]
	public AdminFeatureOptions Features { get; set; } = new();

	/// <summary>
	/// Gets or sets paging constraints for Admin queries.
	/// </summary>
	[Required]
	public AdminPagingOptions Paging { get; set; } = new();

	/// <summary>
	/// Gets or sets projection query constraints for Admin queries.
	/// </summary>
	[Required]
	public AdminProjectionOptions Projections { get; set; } = new();
}

/// <summary>
/// Feature toggles for Admin portal capabilities.
/// </summary>
public sealed class AdminFeatureOptions
{
	/// <summary>
	/// Gets or sets whether aggregate search is enabled.
	/// </summary>
	public bool SearchAggregates { get; set; } = true;

	/// <summary>
	/// Gets or sets whether aggregate details are visible.
	/// </summary>
	public bool ViewAggregate { get; set; } = true;

	/// <summary>
	/// Gets or sets whether event history is visible.
	/// </summary>
	public bool ViewEvents { get; set; } = true;

	/// <summary>
	/// Gets or sets whether point-in-time projection is enabled.
	/// </summary>
	public bool ProjectPointInTime { get; set; } = true;

	/// <summary>
	/// Gets or sets whether event export is enabled.
	/// </summary>
	public bool ExportEvents { get; set; }

	/// <summary>
	/// Gets or sets whether the event-store capability contract and operational health endpoints are
	/// enabled. Opt-in and deny-by-default; a host must also grant the <c>ViewCapabilities</c> permission.
	/// </summary>
	public bool ViewCapabilities { get; set; }

	/// <summary>
	/// Gets or sets whether the poisoned (dead-letter) outbox endpoint is enabled. Opt-in and
	/// deny-by-default; a host must also grant the <c>ViewPoisonedOutbox</c> permission.
	/// </summary>
	public bool ViewPoisonedOutbox { get; set; }

	/// <summary>
	/// Gets or sets whether the runtime event-contract manifest endpoint is enabled. Opt-in and
	/// deny-by-default; a host must also grant the <c>ViewManifest</c> permission and register an
	/// <c>IEventContractManifestProvider</c>.
	/// </summary>
	public bool ViewManifest { get; set; }

	/// <summary>
	/// Gets or sets whether the unknown-event visibility endpoint is enabled. Opt-in and
	/// deny-by-default; a host must also grant the <c>ViewUnknownEvents</c> permission.
	/// </summary>
	public bool ViewUnknownEvents { get; set; }

	/// <summary>
	/// Gets or sets whether the snapshot status endpoint is enabled. Opt-in and deny-by-default;
	/// a host must also grant the <c>ViewSnapshot</c> permission.
	/// </summary>
	public bool ViewSnapshot { get; set; }

	/// <summary>
	/// Gets or sets whether the snapshot rebuild endpoint is enabled. Opt-in and deny-by-default;
	/// a host must also grant the <c>RebuildSnapshot</c> permission.
	/// </summary>
	public bool RebuildSnapshot { get; set; }
}

/// <summary>
/// Paging constraints for Admin list and search queries.
/// </summary>
[ZodSchema]
public sealed partial class AdminPagingOptions
{
	/// <summary>
	/// Gets or sets the default page size.
	/// </summary>
	[Range(1, int.MaxValue)]
	public int DefaultPageSize { get; set; } = 50;

	/// <summary>
	/// Gets or sets the maximum page size.
	/// </summary>
	[Range(1, int.MaxValue)]
	public int MaxPageSize { get; set; } = 200;

	partial void OnZodValidate(RefineCtx<AdminPagingOptions> context)
	{
		if (DefaultPageSize > MaxPageSize)
		{
			context.AddIssue(
				"invalid_range",
				$"{nameof(DefaultPageSize)} must be less than or equal to {nameof(MaxPageSize)}.",
				[nameof(DefaultPageSize), nameof(MaxPageSize)]
			);
		}
	}
}

/// <summary>
/// Projection query limits for Admin projection endpoints.
/// </summary>
[ZodSchema]
public sealed class AdminProjectionOptions
{
	/// <summary>
	/// Gets or sets the maximum number of versions to request in a single projection query.
	/// </summary>
	[Range(1, int.MaxValue)]
	public int MaxVersionsPerQuery { get; set; } = 10000;

	/// <summary>
	/// Gets or sets the maximum time range that can be projected at once.
	/// </summary>
	[Range(typeof(TimeSpan), "0", "365.00:00:00", ConvertValueInInvariantCulture = true)]
	public TimeSpan MaxTimeRangePerQuery { get; set; } = TimeSpan.FromDays(365);
}
