using Purview.EventSourcing.Aggregates;
using Purview.EventSourcing.Aggregates.Persistence;
using ValidationFailure = Purview.EventSourcing.Validation.ValidationFailure;
using ValidationResult = Purview.EventSourcing.Validation.ValidationResult;

namespace Purview.EventSourcing.Failures;

// Guards the failure vocabulary itself: that each union converts its cases into a result, that the case
// survives the round trip, and that a success is not mistaken for a failure. The unions share leaf cases
// deliberately, so these also pin the union-receiver factory as the way shared cases are constructed.
public class AggregateFailureTests
{
	const string AggregateType = "order";
	const string AggregateId = "order-1";

	[Test]
	public async Task Failure_GivenNotFound_ReturnsReadFailureCarryingTheCase()
	{
		// Arrange
		AggregateNotFound notFound = new(AggregateType, AggregateId);

		// Act
		var result = AggregateReadFailure.Failure<string>(notFound);

		// Assert
		await Assert.That(result.IsFailure).IsTrue();
		await Assert.That(result.Error.Value).IsTypeOf<AggregateNotFound>();
		await Assert.That(((AggregateNotFound)result.Error.Value).AggregateId).IsEqualTo(AggregateId);
	}

	[Test]
	public async Task Failure_GivenConcurrencyConflict_CarriesBothVersions()
	{
		// Arrange
		ConcurrencyConflict conflict = new(AggregateType, AggregateId, ExpectedVersion: 3, ActualVersion: 5);

		// Act
		var result = AggregateSaveFailure.Failure<string>(conflict);

		// Assert
		await Assert.That(result.IsFailure).IsTrue();

		var value = (ConcurrencyConflict)result.Error.Value;
		await Assert.That(value.ExpectedVersion).IsEqualTo(3L);
		await Assert.That(value.ActualVersion).IsEqualTo(5L);
	}

	[Test]
	public async Task Failure_GivenInvalidAggregate_CarriesTheValidationResult()
	{
		// Arrange
		ValidationResult validationResult = new([new ValidationFailure("Name", "Name is required.")]);

		// Act
		var result = AggregateSaveFailure.Failure<string>(new AggregateInvalid(validationResult));

		// Assert
		await Assert.That(result.IsFailure).IsTrue();

		var value = (AggregateInvalid)result.Error.Value;
		await Assert.That(value.ValidationResult.IsValid).IsFalse();
		await Assert.That(value.ValidationResult.Failures.Length).IsEqualTo(1);
	}

	[Test]
	public async Task Success_GivenValue_IsNotAFailure()
	{
		// Act
		var result = AggregateSaveFailure.Success("saved");

		// Assert
		await Assert.That(result.IsSuccess).IsTrue();
		await Assert.That(result.IsFailure).IsFalse();
		await Assert.That(result.Value).IsEqualTo("saved");
	}

	[Test]
	[Arguments(SaveOutcomeKind.Written, true)]
	[Arguments(SaveOutcomeKind.NoChanges, false)]
	[Arguments(SaveOutcomeKind.AlreadyApplied, false)]
	public async Task SaveOutcome_GivenKind_ReportsWhetherTheStreamWasWritten(
		SaveOutcomeKind kind,
		bool expectedWritten
	)
	{
		// Arrange & Act
		SaveOutcome<PersistenceAggregate> outcome = new(new(), kind);

		// Assert
		await Assert.That(outcome.Written).IsEqualTo(expectedWritten);
	}
}
