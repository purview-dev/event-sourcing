using Purview.EventSourcing.Validation;

namespace Purview.EventSourcing.Failures;

/// <summary>
/// Validation rejected the aggregate, so nothing was written.
/// </summary>
/// <param name="ValidationResult">The failures that caused the rejection.</param>
public readonly record struct AggregateInvalid(ValidationResult ValidationResult);
