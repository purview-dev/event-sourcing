# Solution Design Worksheet

Use this worksheet before implementing a new aggregate, workflow, or read model. Keep the first version short. The goal
is to expose modelling decisions early, not to produce perfect documentation.

## Business Capability

| Question | Answer |
| --- | --- |
| Capability or workflow name | |
| Primary actor or system | |
| Business outcome | |
| Immediate consistency decisions | |
| Eventually consistent decisions | |
| External systems involved | |
| Audit questions to answer later | |

## Aggregate Boundaries

| Candidate aggregate | Owns these rules | Must not own | Lifecycle starts when | Lifecycle ends when |
| --- | --- | --- | --- | --- |
| | | | | |
| | | | | |

Boundary checks:

- Can this aggregate make its decision using only its own current state?
- Does it need one ordered event stream to stay correct?
- Are other aggregate IDs enough, or are you trying to join live state?
- Would splitting this aggregate allow invalid business states?

## Names

| Concept | Name | Persisted name or note |
| --- | --- | --- |
| Aggregate class | `ExampleAggregate` | Aggregate type becomes `example` |
| Lifecycle-start event | | |
| Important transition event | | |
| Important correction event | | |
| Value object | | |
| Query model/projection | | |

Naming checks:

- Aggregate class ends in `Aggregate`.
- Generated event names should read as past-tense facts.
- Explicit `EventName` overrides are reserved for compatibility, integration contracts, or deliberate domain language.
- Events avoid command-style phrasing unless the business fact genuinely uses that language.
- Persisted names are stable enough to keep after release.

## Commands And Events

| Intent | Aggregate method | Event fact | Payload | Validation |
| --- | --- | --- | --- | --- |
| | | | | |
| | | | | |
| | | | | |

Checks:

- Invalid commands fail before an event is recorded.
- Event payload can replay state without external services.
- Metadata is marked with `[Metadata]` when it should not map to aggregate state.
- Parameter aliases use `[Property(nameof(Property))]`.
- Deterministic generated values use `[Computed]`.
- Collection changes use `[CollectionEvent]` with `EventStoreList<T>` or `EventStoreSet<T>`.
- Manual events identify who owns the `Apply(...)` method.

## Event Stream Example

| Version | Event | Payload summary | Resulting aggregate state |
| --- | --- | --- | --- |
| 1 | | | |
| 2 | | | |
| 3 | | | |
| 4 | | | |

Replay checks:

- Replaying these events in order recreates the expected state.
- Replay does not call external systems.
- Replay does not depend on current time, random values, or database lookups.

## Relationships

| Relationship | Stored identity/value object | Enforced by | Read/query model |
| --- | --- | --- | --- |
| | | | |
| | | | |

Relationship checks:

- Aggregates store IDs/value objects, not other aggregate instances.
- Cross-aggregate rules live in an application service, process manager, or transaction boundary.
- Relational screens are supplied by snapshots, projections, or query models.
- If a workflow spans stores or transaction boundaries, compensating behavior is designed.

## Value Objects

| Value object | Primitive fields | Normalization | Validation | Context needed? |
| --- | --- | --- | --- | --- |
| | | | | |
| | | | | |

Checks:

- Reusable primitive rules are not duplicated across aggregates.
- Contextual value objects separate strict command-time creation from hydration/replay.
- Value object names use business language.

## Validation Map

| Rule | Layer | Failure message/user impact | Test case |
| --- | --- | --- | --- |
| | Value object | | |
| | Public aggregate method | | |
| | Generator hook | | |
| | DataAnnotations/save-time | | |
| | FluentValidation/save-time | | |
| | Application service/process manager | | |
| | Projection/query model | | |

Validation checks:

- Command errors fail before events are recorded.
- Save-time validation covers whole-aggregate consistency.
- Replay/hydration paths do not reject historical facts that were valid when written.
- Validation failures are surfaced from `SaveResult<TAggregate>` where needed.

## Operation Semantics

| Concern | Decision |
| --- | --- |
| Aggregate ID source | |
| Correlation ID propagation | |
| Idempotency marker usage | |
| Principal/claim requirement | |
| Delete/restore/permanent-delete behavior | |
| Snapshot strategy | |
| Change feed notifications | |
| Transaction boundary | |

Transaction checks:

- Shared native transaction boundary is identified where atomicity is required.
- Sequential fallback is acceptable or mitigated.
- Retry and compensation behavior is documented.

## Query And Reporting

| Question/screen/API | Source events or snapshots | Shape | Rebuild strategy |
| --- | --- | --- | --- |
| | | | |
| | | | |

Checks:

- The aggregate is not shaped around a single UI screen.
- Query models can be rebuilt or corrected from event history where practical.
- Sensitive data and retention concerns are noted.
- Snapshots are treated as read/performance models, not as the canonical source of truth.

## Schema Evolution

| Event | Expected future change | Compatibility approach |
| --- | --- | --- |
| | | |
| | | |

Checks:

- Event versioning is considered for breaking payload changes.
- Old event names and aggregate type names are treated as persisted contracts.
- Upcasting or migration notes exist for released event shapes.

## Ready To Build

- Aggregate boundary is clear.
- Event names and payloads are agreed.
- Relationship modelling uses identities and query models.
- Validation has a named owner at each layer.
- Operation semantics are documented.
- Replay path is deterministic.
- Tests are identified for commands, replay, value objects, workflows, and projections.
