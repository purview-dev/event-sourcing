using System.Runtime.CompilerServices;

// This assembly is bundled into each Entity Framework-backed provider package rather than shipped on its
// own, so its types stay internal and are shared with the providers that consume them.
[assembly: InternalsVisibleTo("Purview.EventSourcing.SqlServer")]
[assembly: InternalsVisibleTo("Purview.EventSourcing.Postgres")]
[assembly: InternalsVisibleTo("Purview.EventSourcing.CosmosDb")]
[assembly: InternalsVisibleTo("Purview.EventSourcing.UnitTests")]
