using System.ComponentModel.DataAnnotations;
using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing.Validation;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
	"Performance",
	"CA1849:Call async methods when in an async method",
	Justification = "These tests exercise the synchronous Validate API directly."
)]
public class DefaultAggregateValidatorTests
{
	class NoValidationAggregate : AggregateBase
	{
		public string Name { get; private set; } = string.Empty;

		protected override void RegisterEvents() { }
	}

	class AnnotatedAggregate : AggregateBase
	{
		[Required]
		public string? Name { get; private set; }

		public AnnotatedAggregate() { }

		public AnnotatedAggregate(string name) => Name = name;

		protected override void RegisterEvents() { }
	}

	[Test]
	public async Task Validate_GivenAggregateWithoutAnnotations_ReturnsSuccess()
	{
		// Arrange
		var validator = DefaultAggregateValidator<NoValidationAggregate>.Instance;

		// Act
		var result = validator.Validate(new NoValidationAggregate());

		// Assert
		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async Task ValidateAsync_GivenAggregateWithoutAnnotations_ReturnsSuccess()
	{
		// Arrange
		var validator = DefaultAggregateValidator<NoValidationAggregate>.Instance;

		// Act
		var result = await validator.ValidateAsync(new NoValidationAggregate());

		// Assert
		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async Task Validate_GivenAggregateWithValidAnnotations_ReturnsSuccess()
	{
		// Arrange
		var validator = DefaultAggregateValidator<AnnotatedAggregate>.Instance;

		// Act
		var result = validator.Validate(new AnnotatedAggregate("valid name"));

		// Assert
		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async Task Validate_GivenAggregateWithInvalidAnnotations_ReturnsFailures()
	{
		// Arrange
		var validator = DefaultAggregateValidator<AnnotatedAggregate>.Instance;

		// Act
		var result = validator.Validate(new AnnotatedAggregate());

		// Assert
		await Assert.That(result.IsValid).IsFalse();
		await Assert.That(result.HasPropertyFailures).IsTrue();
	}
}
