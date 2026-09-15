### New Rules

| Rule ID | Category | Severity | Notes |
|---------|----------|----------|-------|
| EVENTSTORE030 | EventContracts | Error | Aggregate event contract removed or renamed relative to an approved baseline |
| EVENTSTORE031 | EventContracts | Error | Event removed or renamed relative to an approved baseline |
| EVENTSTORE032 | EventContracts | Error | Persisted event field removed or renamed relative to an approved baseline |
| EVENTSTORE033 | EventContracts | Error | Persisted event field type changed incompatibly relative to an approved baseline |
| EVENTSTORE034 | EventContracts | Error | Persisted event field became required or non-nullable relative to an approved baseline |
| EVENTSTORE035 | EventContracts | Error | Event schema version regressed below an approved baseline version |
| EVENTSTORE036 | EventContracts | Error | Event contract baseline manifest is malformed or uses an unsupported format version |
| EVENTSTORE037 | EventContracts | Warning | Event type must be marked with [EventContract] |