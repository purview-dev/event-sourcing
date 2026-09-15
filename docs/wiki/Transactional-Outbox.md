# Transactional Outbox

The transactional outbox persists messages atomically with event saves and dispatches them
reliably to downstream consumers. It is an **explicit capability**, not a universal guarantee: only
providers with a relational transaction boundary (SQL Server and PostgreSQL) can write the outbox in
the same native transaction as events.

## Honest semantics

An outbox provides **atomic persistence plus at-least-once delivery**:

- Atomic persistence means a message committed with its events cannot be lost — if the event save
  rolls back, the outbox write rolls back too.
- Delivery is at-least-once: after a crash or lease expiry a message can be dispatched again.
  **Consumers must be idempotent.**

`EventStoreCapabilities.SupportsTransactionalOutbox` reports which registered providers support the
atomic write path; see [Provider Capabilities](Provider-Capabilities.md).

## Registering

```csharp
builder.Services.AddSqlServerOutbox<MyOutboxHandler>(options =>
{
    options.MaxAttempts = 5;
    options.RetryBackoffBase = TimeSpan.FromSeconds(5);
});
```

PostgreSQL uses `AddPostgresOutbox<MyOutboxHandler>`. The hosted dispatch loop is registered
automatically. Outbox table options are bound from `EventStore:SqlServer:Outbox` (or
`EventStore:Postgres:Outbox`) and fall back to the event-store connection string.

## Writing messages atomically with events

Use the provider-native transaction coordinator and enlist the outbox write alongside the aggregate
save:

```csharp
public sealed class OrderService(
    ISqlServerEventStoreTransactionFactory transactionFactory,
    ISqlServerEventStore<OrderAggregate> orderStore,
    SqlServerOutboxStore outboxStore)
{
    public async Task PlaceOrderAsync(OrderAggregate order, CancellationToken cancellationToken)
    {
        var envelope = new OutboxEnvelope(
            Id: Guid.NewGuid().ToString("N"),
            AggregateType: nameof(OrderAggregate),
            AggregateId: order.Id(),
            EventType: "OrderPlaced",
            PayloadJson: "{\"orderId\":\"" + order.Id() + "\"}",
            IdempotencyKey: order.Id(),
            CorrelationId: null,
            CreatedUtc: DateTimeOffset.UtcNow);

        await using var transaction = transactionFactory.CreateSqlServerTransaction();
        transaction.Enlist(order, orderStore);
        transaction.Enlist((connection, sqlTransaction, token) =>
            outboxStore.EnqueueInTransactionAsync(connection, sqlTransaction, envelope, token));

        var result = await transaction.CommitAsync(cancellationToken);
    }
}
```

Enqueuing with the same `IdempotencyKey` again is a no-op (deduplicated at the store).

## Dispatch behavior

- **Leasing/claiming:** a batch is claimed atomically (`UPDATE ... OUTPUT`/`RETURNING`) by a
  lease owner until a lease duration; another dispatcher reclaims only expired leases.
- **Ordering:** messages are claimed oldest-first by `CreatedUtc`, then `Id`.
- **Retry and backoff:** failures increment the attempt count and schedule a retry with exponential
  backoff (`RetryBackoffBase` doubled per attempt, capped at 64x).
- **Poison messages:** after `MaxAttempts` the message moves to the poisoned (dead-letter) state
  with the last error recorded. Poisoned and dispatched messages older than `Retention` are removed
  by `CleanupAsync`.
- **Observability:** every failure and dispatch cycle is logged; the last error is stored on the
  message.
- **Cancellation:** dispatch honors the cancellation token; stopping the host interrupts the loop.

## Concurrent dispatchers

Multiple dispatchers (or hosts) may run concurrently. The lease claim is atomic, so each message is
claimed by exactly one dispatcher at a time. See `SqlServerOutboxIntegrationTests` and
`PostgresOutboxIntegrationTests` for real-provider coverage of atomic commit/rollback, retry/poison,
deduplication, and concurrent claim disjointness.

## Dead-letter visibility

The Admin portal exposes poisoned (dead-letter) messages at `GET /admin/api/outbox/poisoned` when
the `ViewPoisonedOutbox` feature and permission are enabled (opt-in, separately authorized, and
audited). `IOutboxStore.GetPoisonedAsync` returns a page of poisoned messages ordered
most-recently-poisoned first; providers without dead-letter inspection return an empty page.