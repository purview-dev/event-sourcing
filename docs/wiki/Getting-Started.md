# Getting Started

## Install

```bash
dotnet add package Purview.EventSourcing
```

Add one or more provider packages based on your persistence target:

```bash
dotnet add package Purview.EventSourcing.SqlServer
dotnet add package Purview.EventSourcing.Postgres
dotnet add package Purview.EventSourcing.AzureStorage
dotnet add package Purview.EventSourcing.MongoDB
dotnet add package Purview.EventSourcing.CosmosDb
```

Optional packages:

```bash
# In-memory provider (local/test scenarios)
dotnet add package Purview.EventSourcing.InMemory

# Validation adapters
dotnet add package Purview.EventSourcing.Validation.FluentValidation
dotnet add package Purview.EventSourcing.Validation.ZodSharp
```

## Dependency guardrail for Purview.ZodSharp

If your project references the `Purview.EventSourcing.Validation.ZodSharp` project directly and uses `Purview.ZodSharp` types,
you must add:

```xml
<PackageReference Include="Purview.ZodSharp" />
```

`Purview.EventSourcing.Validation.ZodSharp` ships a build-time check (`ValidateZodSharpDirectReference`) in package
`buildTransitive` assets so consumer projects fail fast with remediation guidance when this direct package reference
is missing.

## Define an aggregate (source generator)

```csharp
using Purview.EventSourcing.Aggregates;

[Aggregate]
public partial class OrderAggregate : AggregateBase
{
    public string CustomerId { get; private set; } = default!;
    public decimal Total { get; private set; }

    [Event]
    public partial void CreateOrder(string customerId);

    [Event]
    public partial void AddLineItem(string productId, string productName, int quantity, decimal unitPrice);
}
```

## Register storage

```csharp
// SQL Server / Azure SQL (events + queryable snapshots)
builder.Services.AddSqlServerEventStore();
builder.Services.AddSqlServerSnapshotQueryableEventStore();
```

Other provider registrations:

```csharp
// Azure Storage (event store with blob support)
builder.Services.AddAzureStorageEventStore();

// PostgreSQL (events + queryable snapshots)
builder.Services.AddPostgresEventStore();
builder.Services.AddPostgresSnapshotQueryableEventStore();

// MongoDB (events + queryable snapshots)
builder.Services.AddMongoDBEventStore();
builder.Services.AddMongoDBSnapshotQueryableEventStore();

// Cosmos DB (queryable snapshots)
builder.Services.AddCosmosDbSnapshotQueryableEventStore();

// Core-only fallback for projects without persistent query snapshots
builder.Services.AddNullQueryableEventStore();
```

## Use the provider-agnostic facade

```csharp
public sealed class OrderService(IEventStore store)
{
    public async Task PlaceOrderAsync(string orderId, string customerId, CancellationToken cancellationToken)
    {
        var order = await store.GetAsync<OrderAggregate>(orderId, cancellationToken)
            ?? await store.CreateAsync<OrderAggregate>(orderId, cancellationToken: cancellationToken);

        order.CreateOrder(customerId);
        await store.SaveAsync(order, cancellationToken);
    }
}
```

## Query aggregate event history (time/range filters)

```csharp
var history = await store.GetEventHistoryAsync<OrderAggregate>(
    aggregateId: orderId,
    request: new AggregateEventHistoryRequest
    {
        FromVersion = 10,
        ToVersion = 50,
        FromUtc = DateTimeOffset.UtcNow.AddDays(-7),
        MaxRecords = 100
    },
    cancellationToken: cancellationToken);

foreach (var item in history.Results)
{
    Console.WriteLine($"{item.AggregateVersion} {item.When:u} {item.EventType}");
}
```

## Next pages

- [Guarantees and Limitations](Guarantees-and-Limitations.md)
- [Provider Feature Matrix](Provider-Feature-Matrix.md)
- [Provider Capabilities](Provider-Capabilities.md)
- [Transaction Guarantees](Transaction-Guarantees.md)
- [Event Contract Manifest](Event-Contract-Manifest.md)
- [Dependency Guardrails](Dependency-Guardrails.md)
- [Source Generator Behaviors](Source-Generator-Behaviors.md)
- [Source Generator Code Fixes](Code-Fixes.md)
- [SQL Server Guide](SQL-Server-Guide.md)
- [Release Flow](Release-Flow.md)

If you plan to query snapshot JSON deeply in SQL providers, read the SQL Server guide and provider matrix before relying
on nested predicates through scalar value object `.Value` members.
