---
name: event-sourcing-aggregate-validation
description: "Use when deciding where validation belongs for a Purview.EventSourcing aggregate - command and hook-time invariants, DataAnnotations or FluentValidation at save time via IAggregateValidator, the AggregateInvalid failure case returned by SaveAsync, and computed snapshot mirror properties."
license: MIT
metadata:
  package: Purview.EventSourcing
  category: architecture
---

# Event Sourcing Aggregate Validation Skill

Use this skill when defining validation strategy for event-sourced aggregates in `Purview.EventSourcing`.

## Validation layers to apply

1. **Command/event-raising validation**
   - Enforce transition and intent rules in generated hooks (`OnComputing...Event`, `OnRaising...Event`, `OnShouldApply...Event`, `On<Property>Changing`).
   - Reject invalid operations before events are recorded.

2. **Aggregate model validation**
   - Use DataAnnotations on aggregate properties for structural constraints.
   - Examples: `[Range]`, `[Required]`, length constraints.

3. **Save-time validation**
   - Save pipeline validates aggregates through `IAggregateValidator<TAggregate>`.
   - If no custom validator is provided, `DefaultAggregateValidator<TAggregate>` runs DataAnnotations validation.
   - If a FluentValidation `IValidator<TAggregate>` is provided, it is adapted via `FluentValidationAggregateValidator<TAggregate>`.

4. **Computed mirror validation**
   - For snapshot/query-facing mirror properties, derive values in `[Computed]` hooks instead of trusting caller input.
   - Keep canonical domain properties as the source for recomputing mirrored query shapes.

## Rules

- Keep invariant checks close to mutation points (command/hook layer).
- Keep structural and shape constraints declarative (DataAnnotations/FluentValidation).
- Do not silently swallow validation failures.
- Treat the `AggregateInvalid` failure case, and the `ValidationResult` it carries, as first-class outcomes.
- `SaveAsync` returns `Result<SaveOutcome<TAggregate>, AggregateSaveFailure>`; a rejected save is the
  `AggregateInvalid` case, not an exception. Throw from the call site only if the caller genuinely cannot continue.
- Preserve replay tolerance by avoiding command-time assumptions in hydration-only paths.
- For computed snapshot/query mirrors, validate derivation rules, not user-supplied values.
- If a save-time validator is added, prefer `IAggregateValidator<TAggregate>` over ad-hoc validation inside stores.

## Aggregate validation checklist

- Every command has explicit precondition checks.
- Property transitions are guarded (`On<Property>Changing` / `On<Property>Changed` where needed).
- Aggregate-level attributes capture simple declarative constraints.
- Custom FluentValidation rules are used for cross-field/business policies not suited to attributes.
- Save handlers match the result and act on `AggregateInvalid.ValidationResult`.
- Query-facing mirror properties are recomputed consistently from canonical state.

## Output template to use

1. Validation map by layer (command, aggregate model, save pipeline, computed mirrors).
2. Hook methods and invariants per command (`OnComputing...Event`, `OnRaising...Event`, `OnShouldApply...Event`, `On<Property>Changing`/`Changed`).
3. Attribute-based constraints and rationale.
4. FluentValidation rules (if any) and when they execute.
5. Save-result handling policy (matching `AggregateInvalid`, error propagation).
6. Mirror-property derivation guarantees when query-facing fields exist.
