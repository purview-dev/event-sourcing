using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Admin.Site.UnitTests")]
// The page models are internal - they are the site's implementation, not a contract a consumer binds to -
// and their tests live in the samples suite rather than a matching Admin.Site.UnitTests project.
[assembly: InternalsVisibleTo("Purview.EventSourcing.Samples.UnitTests")]
