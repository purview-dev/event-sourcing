# Purview.EventSourcing.InMemory

`Purview.EventSourcing.InMemory` provides in-memory event and snapshot stores for Purview Event Sourcing. It is intended for development, testing, and prototyping; data is not persisted across process restarts.

## Install

```bash
dotnet add package Purview.EventSourcing.InMemory
```

## Register the stores

### Event store only

Registers the event store against the non-queryable store contracts (`IEventStoreCore<T>`, `INonQueryableEventStore<T>`, `IInMemoryEventStore<T>`) and the non-generic `IEventStore` facade.

```csharp
builder.Services.AddInMemoryEventStore();
```

### Event store with in-memory snapshots

Registers the snapshot store against both the non-queryable and queryable contracts (`IQueryableEventStoreCore<T>`, `IInMemorySnapshotStore<T>`), plus the `IQueryableEventStore` facade. Aggregate state is cached in a snapshot that is rebuilt from events on demand.

```csharp
builder.Services.AddInMemorySnapshotEventStore();
```

## Typical usage

```csharp
var order = await store.GetAsync<OrderAggregate>(orderId, cancellationToken);
if (order is null)
{
    order = await store.CreateAsync<OrderAggregate>(orderId, cancellationToken);
    order.CreateOrder(customerId);
    await store.SaveAsync(order, cancellationToken);
}
```

## What it provides

- `InMemoryEventStore<T>` - event-stream persistence backed by an in-memory collection
- `InMemorySnapshotStore<T>` - snapshot-backed queryable reads plus event-stream persistence
- Transient service registrations, so each resolution gets a fresh store instance

## Limitations

- Data is lost when the process exits.
- The stores are not distributed or durable; do not use them in production workloads.
- Snapshot-backed reads and event persistence are not transactional across processes.

## Documentation

- [Homepage](https://purview.dev/projects/event-sourcing/)
- [Documentation](https://purview.dev/docs/event-sourcing/)

## Related packages

- [Core package](https://github.com/purview-dev/event-sourcing/blob/main/src/src/EventSourcing/Sdk/README.md): `Purview.EventSourcing`
- [Provider feature matrix](https://github.com/purview-dev/event-sourcing/blob/main/docs/wiki/Provider-Feature-Matrix.md)