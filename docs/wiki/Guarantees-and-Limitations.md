# Guarantees and Limitations

This page is the single authoritative summary of what the framework guarantees and where it does
not. Individual topics link to their detailed pages; do not duplicate conflicting claims elsewhere.

## Event ordering and optimistic concurrency

- Event streams are the canonical source of aggregate truth. Snapshots are replaceable
  optimizations or read models.
- Events within a stream are persisted in aggregate-version order. Writes to different aggregates
  never contend.
- Providers detect conflicting writes (optimistic concurrency) and surface them as the
  `ConcurrencyConflict` case on the operation's result; see `ConcurrencyRetry` and
  `AggregateWriteLock` for retry and in-process serialization.
  `EventStoreCapabilities.Concurrency` reports which providers are optimistic versus
  last-writer-wins. This holds whichever row the conflict is detected on - a stream-version
  check or a duplicate event row for an already-persisted version are the same outcome, and a
  shared contract test asserts it against every event-store provider.
- Inside a transaction the same failure arrives on `TransactionAggregateResult.Failure`.
  `Error` carries only genuine exceptions, so a save that failed for a reason the caller can act
  on is never reported as an unexplained `Saved == false`.

## Expected failures are results; exceptions are exceptional

See [Result and Failure Model](Result-And-Failure-Model.md) for the full vocabulary.

- A failure a correct caller can anticipate and act on is a **result case**. A failure that means
  the system, the configuration or the calling code is wrong stays an **exception**. Cancellation is
  always `OperationCanceledException`.
- `GetAsync`, `GetAtAsync`, `GetOrCreateAsync` and `GetDeletedAsync` return
  `Result<T, AggregateReadFailure>`. None of them returns `null`, so "absent" and "deleted" are no
  longer the same answer.
- `SaveAsync`, `DeleteAsync` and `RestoreAsync` return `Result<SaveOutcome<T>, …>`. A success that
  wrote nothing is still a success: `SaveOutcomeKind` distinguishes `Written`, `NoChanges` and
  `AlreadyApplied` (an idempotency marker matched), which the old `bool` conflated with failure.
- The failure cases are shared across every provider, so a caller handles `AggregateNotFound`,
  `AggregateDeleted`, `AggregateLocked`, `ConcurrencyConflict` or `AggregateInvalid` without
  referencing a provider assembly.
- `CommitException` and the provider infrastructure exceptions still throw: storage refusing a write
  is not an outcome a caller can act on.
- `ExistsAsync` returns `AggregateExistence`, which was already a typed outcome. It carries `DoesExist`
  and `IsDeleted` separately, because an aggregate that exists in a deleted state is not the same answer
  as one that exists, and it has no conversion to `bool` that could flatten the two.

## Transaction guarantees and failure modes

See [Transaction Guarantees](Transaction-Guarantees.md) for the full contract.

- `EventStoreTransactionGuarantee.BestEffort`: aggregates are saved sequentially; earlier saves are
  not rolled back on failure.
- `EventStoreTransactionGuarantee.Atomic`: all enlisted aggregates commit or roll back in one
  provider-native transaction (SQL Server and PostgreSQL within one database boundary).
- A transaction that requires a stronger guarantee than the enlisted stores can provide fails
  before any save (`EventStoreTransactionGuaranteeException`). Capability discovery reports the
  actual guarantee per provider.

## Idempotency scope

- Saves deduplicate on an idempotency marker where the provider supports it
  (`EventStoreCapabilities.SupportsIdempotencyMarkers`). Idempotency is scoped to a save operation
  under a correlation/idempotency identifier; it is not a delivery guarantee for downstream
  consumers (which must be idempotent themselves).

## Metadata persistence

Providers persist and expose event metadata where supported
(`EventStoreCapabilities.PreservedMetadata`). The metadata fields are `SchemaVersion`,
`CorrelationId`, `CausationId`, `UserId`, `IdempotencyId`, `AggregateVersion`, and `When`. A field
that is not preserved by a provider is exposed as `null`/default.

## Crypto-shredding

Opt-in. An event implementing `IEventDataSubject` has its payload encrypted (AES-GCM-256) under a key
belonging to that subject, held in `IEventCryptoKeyStore`. Erasing the key erases the subject's data
everywhere it appears, without rewriting or deleting a single event, and without touching any other
subject's data.

- The ciphertext is stored as an envelope **in place of the plaintext payload**, so no provider needs a
  schema change. A payload written before protection was configured is still readable.
- The subject id is bound into the encryption as associated data, so ciphertext cannot be re-attributed
  from one subject to another.
- On replay, a payload whose key has been erased is surfaced as an unknown event: the aggregate version
  still advances, the read succeeds, and the gap is reported through `AggregateBase.SkippedEvents`.
  Replay never fails because somebody exercised their right to erasure.
- Wired for the SQL Server, PostgreSQL, MongoDB and Azure Storage event stores, including the Azure blob
  route used for oversized events.

**Snapshots are disabled while payload protection is enabled.** A snapshot serializes aggregate state,
which holds the same personal data in clear, and one snapshot can span many subjects - so it cannot be
encrypted under any single subject's key. A snapshot surviving key erasure would make crypto-shredding
untrue, so a store configured with an `IEventPayloadProtector` does not write snapshots at all. Replay
from events stays correct; reads of long streams are slower. Existing snapshots written before protection
was enabled are **not** retrospectively removed - delete them as part of enabling protection.

The in-memory store holds live objects rather than serialized payloads, so there is nothing to encrypt
and crypto-shredding does not apply to it.

### Key stores

Keys are **tenant-scoped**: the same data-subject identifier in two tenants is two different people, so
they get two different keys, and neither tenant can read or erase the other's.

| Store | Registration | Durable |
| --- | --- | --- |
| In-memory | `AddInMemoryEventCryptoShredding()` | **No** - keys are lost on restart |
| SQL Server | `AddSqlServerEventCryptoShredding()` | Yes |
| PostgreSQL | `AddPostgresEventCryptoShredding()` | Yes |
| Azure Storage | `AddAzureStorageEventCryptoShredding()` | Yes |
| MongoDB | `AddMongoDBEventCryptoShredding()` | Yes |
| Azure Key Vault (secret per subject) | `AddAzureKeyVaultEventCryptoShredding()` | Yes |
| Azure Key Vault (envelope) | `AddAzureKeyVaultEnvelopeEventCryptoShredding<TInner>()` | Yes |

**The host refuses to start with a non-durable key store outside Development.** Losing the keys is
indistinguishable from erasing every data subject at once, and nothing reports that it happened, so the
only safe moment to object is before any event has been written under a key that is about to be lost.
Set `EventStore:Cryptography:AllowNonDurableKeyStore` to override, if that outcome is genuinely
acceptable. A store declares itself non-durable by implementing `INonDurableEventCryptoKeyStore`, so a
custom store is covered by the same guard.

**Erasure is only as irreversible as the least-irreversible copy of the key.** A point-in-time restore
of the key table brings shredded keys back. A deployment that must erase on request has to exclude the
key table from long-retention backups, or accept that erasure completes only when the last backup
containing the key ages out. This is an operational control, not something the framework can enforce.

Keys are **cached in process memory** by default (`EventStore:Cryptography:CacheKeys`), because an
uncached store makes every protect and unprotect a round trip - impractical against a throttled vault.
`ShredAsync` evicts before deleting and again afterwards, so a reader racing the delete cannot leave a
stale entry behind.

**The cache is per process**, so `KeyCacheDuration` (default 5 minutes) is the upper bound on how long
an erasure takes to take effect across a cluster: another instance holding the key keeps serving it
until its own entry expires. Treat that duration as a compliance parameter rather than a tuning one, or
turn caching off where an erasure must be immediate everywhere. Key material is never written to a
distributed cache.

### Rotation

**SQL Server and PostgreSQL only.** A subject can hold several key generations; the highest is
current and is used for new payloads. `ProtectedPayload` records which generation it was encrypted
under (`kid`), so **rotation never makes existing events unreadable** - earlier generations are
retained deliberately.

Rotation limits the blast radius of a future key compromise. It does **not** re-encrypt history, and
it is distinct from erasure: `ShredAsync` removes *every* generation, because one left behind would
mean the subject's older events were still readable.

A payload written before rotation existed carries no `kid`, which a reader treats as "the subject's
only key" - so the field is backward compatible and needs no upcaster.

A store that cannot hold multiple generations - Azure Storage, MongoDB, both Key Vault stores, and
the in-memory one - simply does not implement `IRotatingEventCryptoKeyStore`, and payloads are
written without a key identifier exactly as before.

## Multi-tenancy

Opt-in, and **currently enforced only by the SQL Server and PostgreSQL providers**.

- `ICurrentTenant` supplies the `TenantScope` an operation runs under. The default registration reports
  `TenantScope.System`, which applies no filter, so a single-tenant deployment is unaffected.
- A multi-tenant host registers its own `ICurrentTenant` - typically resolving the tenant from a claim on
  the current principal - before `AddEventSourcing()`.
- `TenantScope.System` is a distinct state rather than a null tenant id, so a resolver that *fails* to
  determine a tenant can never be mistaken for permission to read every tenant. Use it for infrastructure
  work that deliberately crosses tenants; `AmbientCurrentTenant.BeginScope` changes scope temporarily,
  which is how a super user selects a tenant.
- Rows carry a `TenantId`, stamped on write, and reads are constrained by an Entity Framework global query
  filter. The filter is the enforcement point: a read that forgets the tenant is the failure this exists to
  prevent, so it is applied to the entity rather than to individual queries.
Enforced by every event store - SQL Server, PostgreSQL, MongoDB, Azure Storage and in-memory - and by the
SQL Server, PostgreSQL, MongoDB and Cosmos DB queryable snapshot stores. The mechanism differs by what the
store can express, but the guarantee does not:

| Store | Mechanism |
| --- | --- |
| SQL Server, PostgreSQL | `TenantId` column with an Entity Framework global query filter |
| MongoDB | `TenantId` field composed into every read and delete filter |
| Azure Storage | `TenantId` property, composed into the OData filter; point reads filtered on read-back |
| Cosmos DB snapshots | `__tenantId` shadow property, applied after `WithPartitionKey` |
| In-memory | Ownership tracked beside the data |

**Azure Storage snapshots are blob-backed and are not tenant-filtered.** Everything else above is.

### A tenant-less deployment

Registering no `ICurrentTenant` leaves the scope as `TenantScope.System`: every filter short-circuits, no
tenant predicate is applied, and writes record no owner. A single-tenant deployment therefore behaves
exactly as it did before tenancy existed, and needs no configuration.

**An unowned row is visible only to the system scope.** The filters deliberately do *not* treat a null
`TenantId` as visible to every tenant. That choice matters more than it looks: if any write path ever
fails to stamp the tenant, the row becomes invisible to its own tenant - an obvious, reported absence -
rather than silently readable by all of them. Fail-closed turns a stamping bug into missing data instead
of a cross-tenant leak.

The consequence is that **enabling tenancy over data written while tenant-less hides that data from
tenant-scoped reads**. The system scope still sees it. Backfill `TenantId` on existing rows as part of
enabling tenancy, or read that data under `TenantScope.System`.

### Admin portal

- `AdminPortalOptions.EnforceTenantScope` (off by default) requires every admin request to resolve to a
  single tenant. `IAdminTenantResolver` decides it from the caller's entitlements and the tenant named in
  the `Purview-Tenant` header; the default implementation reads tenant claims.
- An administrator entitled to one tenant is scoped to it. One entitled to several - a super user - must
  name the tenant; naming none is refused with `400`, and naming an unentitled tenant with `403`. **There
  is no fallback that serves every tenant**: the resolver returns a tenant identifier and nothing else, so
  the system scope is unreachable from a signed-in session.
- The filter is applied to the whole admin route group before any endpoint is mapped, so an endpoint added
  later cannot escape tenant scoping by omitting it.

## Subscriptions

A [subscription](Subscriptions.md) reads the event stream itself, so it delivers events written before
the subscriber existed - the thing neither the change feed nor the outbox can do.

Guaranteed: **at-least-once** delivery (the position is written after the subscriber returns, so a crash
redelivers rather than skips), **version order within one aggregate**, and **tenant isolation** across
discovery, the event read and the recorded position.

Not guaranteed, and not configurable:

- **No ordering between aggregates.** Streams are per-aggregate on every provider and there is no global
  sequence. A projection that depends on the relative order of two different aggregates' events cannot be
  built on a subscription.
- **A subscriber that throws stalls that stream**, at the failing event, retried every cycle. This is
  fail-closed by choice: skipping would lose data silently. An unresolvable event type is not a
  failure - it is delivered with `IsUnresolved` set and skipped past.
- **Dead-lettering exists but is off by default**, and needs two deliberate steps: a non-zero
  `SubscriptionOptions.MaxDeliveryAttempts` *and* a registered `ISubscriptionDeadLetterSink`. The
  checkpoint advances past a failed event only when the sink returns `true`; a sink that refuses or
  throws leaves the stream stalled, which is the safe outcome. `DeadLetteredEvent` identifies the
  event rather than copying its payload, so the stream stays the only copy. **The framework ships no
  durable sink** - that is the application's - and attempt counts are kept in memory, so a restart
  grants another `MaxDeliveryAttempts` deliveries.
- **Durable checkpoints ship for every provider with an event stream** — SQL Server, PostgreSQL, Azure Storage and MongoDB, each via `Add<Provider>SubscriptionCheckpoints()`. Without one the default is in-memory and replays every stream after a restart.
- **Cosmos DB cannot be subscribed to** — it persists no event stream. Every other provider works, because
  stream discovery reads `IEventStoreCore<T>.GetAggregateIdsAsync`, which every store implements and
  tenant-filters.
- **Every cycle enumerates every stream** of the aggregate type: the work is linear in the number of
  aggregates, not the number of new events. With no global sequence there is nothing cheaper to read.

Subscriptions are deliberately absent from `EventStoreCapabilities`: whether one can run depends on the
registered discovery implementation, which is dependency-injection state rather than a property of the
store, so a capability flag could not report it truthfully.

### Idempotency marker retention

`IIdempotencyMarkerMaintenance.CleanupIdempotencyMarkersAsync` removes markers older than a window,
on SQL Server, PostgreSQL, Azure Storage and MongoDB. Markers are **opt-in** in the first place
(`EventStoreOperationContext.UseIdempotencyMarker`), so a deployment that never enables them has
nothing to clean.

Deleting an expired marker is safe, which is what makes this separable from event-stream retention:
the worst case is that a save retried after the window elapsed is applied twice, and optimistic
concurrency already rejects that. A zero or negative retention is refused, because it would delete
markers for saves still in flight.

Cleanup honours the current tenant - a tenant clears its own markers, `TenantScope.System` clears
them all. That matters more than a read filter would: a bulk delete that forgot the tenant would
destroy another tenant's data rather than expose it.

**Nothing invokes it.** There is no scheduled cleanup; call it from a maintenance job.

### Event-stream retention

`IEventStreamRetentionPreview<T>.PreviewAsync` reports what an `EventRetentionPolicy` would remove;
`IEventStreamRetention<T>.PruneAsync` removes it. The prune decides through the same preview, so
**what a dry run reported is what a prune does** - run the preview, read the blockers, then prune.

**Nothing is destructive until `EventRetentionOptions.Mode` says so**, and it defaults to `None`:
adding the registration must not start deleting history. `OperatorInvoked` and `Scheduled` are
independent flags, so a deployment can enable either, both, or neither. The scheduled run uses
`TenantScope.System`, because a timer has no signed-in user to take a tenant from.

Every run is logged, per stream rather than per run - a single total cannot answer "what happened to
this aggregate". A run with `RequireSnapshot` disabled is logged as a **warning** naming the risk:
that configuration removes events with no snapshot behind them, and the state they carried cannot be
reconstructed. It is permitted because a deployment may have accepted that, and it is loud because
nobody should be able to say afterwards that they were not told.

On Azure Storage, pruning an oversized event deletes its blob as well as the pointer row; otherwise
the blob would leak storage nothing will ever reference again.

A stream is reported as blocked when there is no covering snapshot, when the events are newer than
`MinimumAge`, when a subscription has not read past the prune point, or when the stream is too short
for the `RetainedVersions` margin. The prune point is the most conservative version all the rules
allow, never the most generous.

Two limits worth knowing. The preview **reads every stream** of the aggregate type, because a
snapshot version is only knowable from the aggregate - run it deliberately, not on a timer. And
subscription lag is checked against the subscriptions **registered in this process**: one running on
another host is invisible, so a deployment that spreads subscriptions across hosts must evaluate
retention where they all are.

Event-stream **retention** is not implemented. Streams grow without bound; nothing removes an event.

## Tracing

Every provider that persists an event stream emits the same spans - `GetAggregate`,
`GetAggregateAtVersion`, `SaveAggregate`, `DeleteAggregate` - with the aggregate id and type. **Span
names are a compatibility contract**, like instrument names: a trace query built against one provider
keeps working when a service moves to another.

Cosmos DB emits `SnapshotAggregate` and `QuerySnapshots` instead. It persists no event stream and
delegates aggregate reads and writes to an inner event store, which emits the aggregate-level spans;
emitting them here as well would double-count every operation in a trace.

The in-memory store emits nothing, deliberately - it exists for tests, and instrumenting it adds noise
to every test run for no diagnostic value.

Aggregate ids appear in spans on every provider. That has always been true of SQL Server and
PostgreSQL; the other providers now match rather than diverge. If an aggregate id is sensitive in your
domain, note that it reaches whatever trace backend you export to, which usually has different
retention and access rules from the database.

**No test asserts that spans are emitted.** The provider fixtures inject mocked telemetry, so a test
listening for activities records nothing; the wiring is covered by compilation and by the provider
suites exercising those paths. See [Observability Parity](Observability-Parity-Design.md).

## Metrics

Every storage provider records the same instruments on the `Purview.EventSourcing` meter, so a dashboard
built against one provider keeps working against another. Instrument and tag names are a compatibility
contract.

| Instrument | Kind | Tags |
| --- | --- | --- |
| `purview.eventsourcing.save.duration` | Histogram (ms) | `aggregate.type`, `outcome` |
| `purview.eventsourcing.get.duration` | Histogram (ms) | `aggregate.type` |
| `purview.eventsourcing.events.appended` | Counter | `aggregate.type` |

`outcome` is `written`, `no-changes`, `already-applied`, or the failure case - `concurrency-conflict`,
`locked`, `deleted`, `invalid`. Only a returned result is recorded; an exception escaping a save is an
infrastructure fault, already reported through logging and the activity. `events.appended` counts only a
real write, so an empty save or an idempotent replay records nothing rather than a zero.

Recording requires the host to have configured metrics (`IMeterFactory`, registered by
`AddMetrics()` and by default in ASP.NET Core). Without it the recorder is a no-op: metrics are never a
precondition for persisting events.

## Originating user

`EventMetadata.UserId` is read from the claim named by `EventStoreOperationContext.ClaimIdentifier`, taken
from the `IEventStorePrincipalAccessor` registered in dependency injection. The default accessor reads the
ambient `ClaimsPrincipal.Current`, which ASP.NET Core never populates, so a web application must register
its own; see [Getting Started](Getting-Started.md#record-who-made-the-change). With
`RequiresValidPrincipalIdentifier` enabled and no identifier available, the save fails rather than
recording an anonymous change.

## Schema evolution, manifests, and upcasters

- [Event-Versioning-Strategy.md](Event-Versioning-Strategy.md) describes how to evolve event
  schemas.
- [Event-Contract-Manifest.md](Event-Contract-Manifest.md) describes the deterministic contract
  manifest and baseline validation that fails a build on breaking changes.
- Upcasters translate legacy payloads during replay. Treat emitted event names, serialized
  payloads, schema versions, and generated method signatures as compatibility-sensitive contracts.

## Snapshot compatibility and safe rebuild

See [Snapshot-Schema-Versioning.md](Snapshot-Schema-Versioning.md).

- Snapshots must always be reconstructible from the event stream.
- `[SnapshotSchemaVersion]` and `AggregateSnapshotSchema` drive version-aware snapshot storage.
  Incompatible snapshots are ignored before deserialization and canonical event replay is used
  instead; a later snapshot-eligible save writes a compatible replacement.

## Provider capability discovery

See [Provider-Capabilities.md](Provider-Capabilities.md). Resolve
`IEventStoreCapabilitiesProvider` from DI to query transaction guarantee, snapshot behavior,
preserved metadata, query support, idempotency, concurrency, and operational limitations for the
registered stores. The [Provider Feature Matrix](Provider-Feature-Matrix.md) summarizes the same
facts for package selection.

## Admin audit

Every privileged Admin operation is recorded as an `AdminAuditEntry`. Entries carry the tenant the
operation **acted on** and a correlation identifier, both stamped centrally rather than at each call
site, so an endpoint added later cannot omit them.

| Store | Registration | Durable |
| --- | --- | --- |
| In-memory | the default | **No** - entries die with the process |
| SQL Server | `AddPurviewEventSourcingAdminSqlServerAudit()` | Yes |
| PostgreSQL | `AddPurviewEventSourcingAdminPostgresAudit()` | Yes |
| Azure Storage | `AddPurviewEventSourcingAdminAzureStorageAudit()` | Yes |
| MongoDB | `AddPurviewEventSourcingAdminMongoDBAudit()` | Yes |

**Audit writes default to refusing the operation when they fail** (`AdminAuditFailureMode.Fail`): an
action nobody can prove happened is worse than an action that did not happen. Set the failure mode to
`Continue` where availability matters more; the failure is then logged and the operation proceeds
unaudited.

The durable tables are append-only by contract - there is no update or delete path - but the framework
cannot grant or revoke database permissions. A deployment that needs tamper-evidence should also limit
the application principal to `INSERT` and `SELECT` on the audit table. A `PreviousHash` column is
reserved so hash chaining can be added later without a migration.

**The read side is `IAdminAuditEntryReader`, and it is tenant-filtered.** Each registration above
also registers a reader over the same store; `AdminAuditQuery` narrows by time, feature, principal,
target, success and correlation, and returns an `AdminAuditPage` with `HasMore` rather than a total
count. `PageSize` is clamped to 500.

- The tenant comes from `ICurrentTenant`, not from the query, so a caller cannot widen their own
  scope. A tenant sees only its own entries; `TenantScope.System` sees all of them.
- **Unowned entries fail closed**: an entry with no `TenantId` is visible only to the system scope,
  never to every tenant. One missed stamp must look like absent data, not like another tenant's trail.
- On Azure Storage the isolation is **structural** - the tenant is the partition key, so another
  tenant's entries are never fetched. Its row key is inverted ticks, so a tenant-scoped read is
  newest-first with no sort. A **system-scope** read there spans partitions, is not globally ordered
  by the store, and is sorted in memory; bound it with `FromUtc`. SQL Server, PostgreSQL and MongoDB
  order in the store for both scopes.

**There is deliberately no Admin API endpoint for the audit trail.** Who may read the record of who
read what is a deployment decision, not a framework default; the reader is public so an application
can expose it on its own terms.

**Audit retention is not implemented** - nothing deletes an entry, on any provider, ever. That is
safe by default and unbounded by default; pruning the audit store is a database-administration task,
and no delete path is offered that could be mistaken for a sanctioned one. See
[Durable Admin Audit](Admin-Audit-Durability-Design.md).

## Admin security, metadata/payload separation, and deny-by-default

- Admin endpoints are denied by default and authorized per feature
  (`AdminFeature`, `AdminPortalPolicies`). `AdminEndpointOptions` lets a host map a feature to its
  own named authorization policy.
- `ViewEvents` grants metadata access; event **payloads** are only returned with
  `ViewEventPayloads` permission. Without it, payloads are `null`.
- Event export requires both export and payload permissions. Read permissions never imply mutation
  authority. Export is capped at `AdminProjectionOptions.MaxVersionsPerQuery`; a truncated stream is
  signaled with the `Purview-Event-Export-Truncated` response header so callers can detect partial
  exports.
- Operational endpoints (`GET /admin/api/capabilities`, `GET /admin/api/health`,
  `GET /admin/api/manifest`, `GET /admin/api/outbox/poisoned`,
  `GET /admin/api/aggregates/{aggregateType}/{aggregateId}/events/unknown`,
  `GET /admin/api/aggregates/{aggregateType}/{aggregateId}/snapshot`, and the opt-in mutation
  `POST /admin/api/aggregates/{aggregateType}/{aggregateId}/snapshot/rebuild`) are opt-in,
  separately authorized, and audited through `IAdminAuditLogger` (default in-memory; replace with a
  durable implementation in production). Health probes the configured storage through the provider's
  `IAdminStorageProbe` and reports `Healthy`, `Unhealthy`, or `Unknown` when no probe is registered;
  the probe failure is logged rather than returned, because a storage error can carry endpoint or
  credential detail. The manifest endpoint reports the runtime event-contract manifest
  and its compatibility status against a supplied baseline. A snapshot rebuild reconstructs the
  aggregate from its canonical event stream and persists a fresh snapshot; it is idempotent and
  requires both an event-backed `IEventStore` and a registered `IQueryableEventStore`.

## Query consistency and provider-specific translation limitations

- Queryable stores are snapshot-backed read models; consistency is as-of-replay, not transactional.
- SQL snapshot translation supports deep predicates over directly mapped JSON graphs, but
  provider-converted scalar value objects may not translate deep members through `.Value`; see the
  [Provider Feature Matrix](Provider-Feature-Matrix.md) and [SQL Server Guide](SQL-Server-Guide.md)
  for exact limits.
- The Cosmos DB snapshot store shares that Entity Framework mapping, so the same member shapes are
  supported and rejected. It differs in paging: continuation tokens are native Cosmos DB tokens -
  opaque, forward-only, and `null` on the final page - and Cosmos DB reports no total count while
  paging. Ordering by more than one member, or ordering alongside a range filter, needs a matching
  composite index declared through `CosmosDbIndexOptions.CompositeIndices`.

## Unknown-event handling and recovery

- Replay of an unknown event type does not corrupt the stream: the aggregate reports
  `AggregateBase.SkippedEvents` so callers can detect partial reconstruction.
- Event-schema versioning and upcasters are the recovery path for payload evolution; the contract
  manifest prevents accidental breaking changes.
- The Admin portal can report stored event type names the runtime cannot resolve to a registered
  event type (`GET /admin/api/aggregates/{aggregateType}/{aggregateId}/events/unknown`, opt-in via
  `ViewUnknownEvents`). Legacy event types handled only by an upcaster may appear in this report
  because they are not registered current event types.