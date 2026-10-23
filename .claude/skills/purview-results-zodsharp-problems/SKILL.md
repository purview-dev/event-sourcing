---
name: purview-results-zodsharp-problems
description: "Use when a Purview.Results failure that carries ZodSharp validation errors has to render as an ASP.NET Core HttpValidationProblemDetails — registering AddResultsZodSharpHttp, calling ToValidationProblem, or making a result-carried rejection and a thrown ZodException produce identical responses."
---

# Rendering validation-carrying failures as validation problems

Use this skill when a result failure carries ZodSharp `ValidationError` values (it implements
`IValidationErrorCarrier`) and the host has to respond with `application/problem+json`.

## Registration

```csharp
builder.Services.AddZodSharpProblemDetails();   // the ZodSharp package's own problem options/exception handler
builder.Services.AddResultsHttp();              // the result-to-response mapping
builder.Services.AddResultsZodSharpHttp();      // the bridge: validation-carrying failures -> validation problem
```

- `AddResultsZodSharpHttp()` registers the ZodSharp problem options (a no-op when the host already did) and adds
  a **failure mapper** (`ZodResultsFailureMapper`) to the fallback stage.
- Because it is a failure mapper, **a mapping the host registered for a specific error case always wins**, a
  mapping registered for the error type wins too, and a host failure mapper registered *before*
  `AddResultsZodSharpHttp` wins as well. That is the escape hatch: when a case deserves its own response, register
  it — and when a validation **code**, **category** or **origin** deserves one, use the rules below.
- Pair it with `AddZodSharpProblemDetails()` so a thrown `ZodException` and a result-carried rejection are
  rendered by the same mapper and therefore produce identical responses.

## Rules per validation error code, category and origin

Pass a callback to answer particular codes, categories or origins with a response of their own:

```csharp
builder.Services.AddResultsZodSharpHttp(options => options
    .MapCode("tenant_not_found", StatusCodes.Status404NotFound)                 // validation problem, 404 default
    .MapCode("tenant_id_matches_name", (errors, context) => TypedResults.Conflict())
    .MapCategory("invalid_value", StatusCodes.Status422UnprocessableEntity)
    .MapOrigin("value_object", StatusCodes.Status422UnprocessableEntity)        // every type-level rule failure
);
```

The three axes are the identity a `ValidationError` carries: **code** (the specific rule), **category** (a broad
grouping many codes share) and **origin** (the structured origin a rule owns — `"value_object"` for a type-level
rule, `"array"` for a collection rule). Reach for `MapOrigin` when the answer is keyed by the *kind* of rule
rather than one code.

Matching and precedence:

1. every matching **code** rule in registration order,
2. then every matching **category** rule in registration order,
3. then every matching **origin** rule in registration order,
4. then the default validation problem — so rules only ever narrow what the host already gets.

- A rule matches when **any** of the failure's errors carries its code/category/origin, so a registered rule is
  always reachable; a factory that wants stricter semantics returns `null` to decline and matching continues.
- A factory receives the failure's **whole error set**, so a rule never hides the other problems the caller has
  to fix; the `int statusCode` overloads still render every error.
- A code, category or origin registered twice with different behaviour throws at configuration time.

## Rendering by hand

| Member | Purpose |
| --- | --- |
| `IValidationErrorCarrier.ToValidationProblem(context, statusCode = 400)` | Renders the errors the error value carries |
| `ImmutableArray<ValidationError>.ToValidationProblem(context, statusCode = 400)` | Renders a set of errors |
| `ZodValidationProblems.ToProblem(errors, options, defaultStatusCode = 400, traceId = null)` | The underlying mapper, for hosts that resolve the options themselves |

```csharp
if (result.Error is IValidationErrorCarrier carrier)
    return carrier.ToValidationProblem(context);
```

The response is written as `application/problem+json` through `TypedResults.Json`, so it does not depend on the
MVC problem-details writer.

## Status code and trace identifier

- The status code resolved by `ZodProblemDetailsOptions.StatusCodeSelector` wins.
- `statusCode` (or `defaultStatusCode`) is only used when the resolved error type does not define one — the
  default is `400 Bad Request`.
- The request trace identifier is included when `ResultsHttpOptions.IncludeTraceId` is `true` (the default).

## Pitfalls

- **Do not reimplement error-to-problem mapping.** Reuse `ZodValidationProblems.ToProblem` so a carried failure
  and a thrown `ZodException` cannot drift apart in status code, title, `errors` shape or `issues` extension.
- **Do not map the whole union to a validation problem** unless every case is a validation failure; map the
  carrying case (or rely on the mapper, which already does exactly that).
- If a case does deserve its own response, still build it from `ToValidationProblem(context)` when the payload
  is validation data, so clients see one shape.
- The mapper receives the *case* value, so the carrier is the union case that implements the interface — not
  the union itself (`ResultsFailureContext.Error` carries the union when a rule needs it).

## Verifying the pairing

A test that exercises both paths is the cheapest guard against drift:

1. return a failure carrying validation errors through an endpoint, and
2. throw the same errors as a `ZodException` through the host's exception handler,

then assert the two response bodies are equal. See the `purview-results-http-mapping` skill for the surrounding
registration and status-code guidance.
