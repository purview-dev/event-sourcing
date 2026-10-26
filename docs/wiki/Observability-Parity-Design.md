# Observability Parity — Design

> **Status: proposal. Nothing on this page is implemented.**

## The problem

Observability is not at parity across providers, and the documentation implies more coverage than
exists. Measured from the telemetry interfaces in each provider:

| Provider | Tracing (`[ActivitySource]`) | Logging (`[Logger]`) | Provider metrics (`[Meter]`) |
| --- | --- | --- | --- |
| SQL Server | Yes | Yes | Yes |
| PostgreSQL | Yes | Yes | Yes |
| MongoDB | **No** | Yes | Yes |
| Azure Storage | **No** | Yes | **No** |
| Cosmos DB | **No** | **No** | Yes |
| InMemory | **No** | **No** | **No** |

The cross-provider `IEventStoreMetrics` — `save.duration`, `get.duration`, `events.appended` — *is*
uniform, and [Guarantees and Limitations](Guarantees-and-Limitations.md) correctly says so. The gap
is **distributed tracing**, which exists on two providers of five.

That is the capability you need when a save is slow in production: metrics tell you *that* it is
slow, a trace tells you *where*. On Azure Storage, MongoDB and Cosmos DB there is currently no
answer.

## Design

No new abstraction. Each provider already has a telemetry interface implemented by
`Purview.Telemetry`'s generator; the work is to bring each up to the same three attributes and the
same span names, following `ISqlServerEventStoreTelemetry` as the template.

### Span naming is a compatibility contract

Instrument names already are one. Span names must be too, or a dashboard built against SQL Server
breaks when the same service moves to MongoDB. Proposed convention, matching the existing SQL
spans:

```text
EventStore.Get
EventStore.Save
EventStore.Delete
EventStore.Restore
EventStore.GetEventRange
EventStore.Snapshot
```

with attributes `aggregate.type`, `aggregate.id`, `event.count`, `tenant.id` and the outcome.

**`aggregate.id` and `tenant.id` are a decision, not an obvious yes.** An aggregate id can be a
natural key and a tenant id identifies a customer; both end up in a trace backend with a different
retention and access model from the database. Recommendation: `tenant.id` always,
`aggregate.id` behind an option defaulting to off, consistent with how a careful HTTP
instrumentation treats a URL path.

### Where spans belong

One span per public store operation, started in the provider implementation, not in the facade —
the facade cannot see provider-specific retries or batch splits, which is exactly the detail a
trace is for. Nested spans for provider-native work worth seeing: batch submission on Azure,
transaction scope on SQL, replica-set round trips on Mongo.

### InMemory

Deliberately left without telemetry. It exists for tests and local development, and instrumenting
it adds noise to every test run for no diagnostic value. This should be *documented* as a decision
rather than left looking like an omission.

## Phases

| Phase | Work |
| --- | --- |
| 1 | Agree span names and attributes; document them as a contract alongside the metric names |
| 2 | Azure Storage: add `[ActivitySource]` and `[Meter]` to `ITableEventStoreTelemetry` |
| 3 | MongoDB: add `[ActivitySource]` |
| 4 | Cosmos DB: add `[ActivitySource]` and `[Logger]` |
| 5 | Update `Guarantees-and-Limitations.md` and the provider guides to state the real coverage |

Phase 5 is worth doing **first** if the others are deferred: documenting the gap honestly costs
nothing and stops someone designing around parity that does not exist.

## Testing

The existing telemetry mocks (`ITableEventStoreTelemetryMock` and friends) already make this
assertable per provider.

- Each provider's contract suite asserts that a save and a get **start the expected span**, with the
  expected attributes — added to the shared contract tests so a new provider cannot omit them.
- `aggregate.id` is absent unless the option is enabled; `tenant.id` is present and correct under
  both a tenant scope and the system scope.
- A failed operation records the failure on the span rather than leaving it successful.

Putting the span assertions in the shared contract suite is the same structural move as the
tenancy seam: parity that is asserted centrally cannot be forgotten by the next provider.

## Decisions needed

1. Whether **`aggregate.id`** is recorded by default. (Recommendation: no, behind an option.)
2. Whether span names follow the `EventStore.*` convention above or OpenTelemetry's database
   semantic conventions (`db.operation` etc.), which would be more portable but less readable.
3. Whether **InMemory** stays uninstrumented. (Recommendation: yes, documented.)
4. Whether provider-native nested spans are in scope for the first pass or follow later.
