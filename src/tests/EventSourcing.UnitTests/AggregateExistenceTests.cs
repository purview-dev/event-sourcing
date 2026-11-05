namespace Purview.EventSourcing;

public class AggregateExistenceTests
{
	[Test]
	[Arguments(ExistenceStatus.Exists, true, false)]
	[Arguments(ExistenceStatus.ExistsInDeletedState, true, true)]
	[Arguments(ExistenceStatus.DoesNotExist, false, false)]
	public async Task Status_GivenEachState_ReportsExistenceAndDeletion(
		ExistenceStatus status,
		bool expectedDoesExist,
		bool expectedIsDeleted
	)
	{
		// Act
		AggregateExistence existence = new(status, Version: null);

		// Assert
		await Assert.That(existence.DoesExist).IsEqualTo(expectedDoesExist);
		await Assert.That(existence.IsDeleted).IsEqualTo(expectedIsDeleted);
	}

	[Test]
	public async Task IsDeleted_GivenExistsInDeletedState_IsDistinctFromDoesExist()
	{
		// A deleted aggregate exists and is deleted; a single boolean cannot carry both, which is why
		// there is no conversion to bool.
		var existence = AggregateExistence.ExistsInDeletedState;

		await Assert.That(existence.DoesExist).IsTrue();
		await Assert.That(existence.IsDeleted).IsTrue();
	}
}
