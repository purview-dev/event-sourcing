## Release 2.0.0

### New Rules
| Rule ID | Category | Severity | Notes |
|---------|----------|----------|-------|
| EVENTSTORE001 | Aggregates | Error | Aggregate must be partial |
| EVENTSTORE002 | Aggregates | Error | Aggregate must inherit AggregateBase |
| EVENTSTORE003 | Aggregates | Error | Nested aggregates are not supported |
| EVENTSTORE004 | Aggregates | Error | Generic aggregates are not supported |
| EVENTSTORE005 | Aggregates | Error | RegisterEvents is generated automatically |
| EVENTSTORE006 | Aggregates | Error | Event requires Aggregate |
| EVENTSTORE007 | Aggregates | Error | Generated event method must be partial |
| EVENTSTORE008 | Aggregates | Error | Unsupported generated event method signature |
| EVENTSTORE009 | Aggregates | Error | Generated event names must be unique |
| EVENTSTORE010 | Aggregates | Error | Generated event parameters must map to writable aggregate properties |
| EVENTSTORE011 | Aggregates | Error | Aggregate property setters should be private |
| EVENTSTORE012 | Aggregates | Warning | Aggregate methods should be verb phrases |
| EVENTSTORE013 | Aggregates | Warning | Event names should be past tense |
| EVENTSTORE014 | Aggregates | Warning | Event name overrides should be past tense |
| EVENTSTORE015 | Aggregates | Warning | Unable to infer a past-tense event name |
| EVENTSTORE016 | Aggregates | Info | Event parameter nullability differs from aggregate property |
| EVENTSTORE017 | Aggregates | Error | Computed parameter cannot be set by caller |
| EVENTSTORE018 | Aggregates | Error | Aggregate collection properties must use EventStore collections |
| EVENTSTORE019 | Aggregates | Warning | Use pattern matching for nullable scalar null checks |
| EVENTSTORE020 | Aggregates | Warning | Complex scalar Value paths may not translate in SQL snapshot queries |
| EVENTSTORE021 | Aggregates | Error | Event schema version must be positive |
| EVENTSTORE022 | Aggregates | Error | Duplicate event schema version on aggregate |
| EVENTSTORE110 | Aggregates | Error | Unable to find reference to AggregateBase |