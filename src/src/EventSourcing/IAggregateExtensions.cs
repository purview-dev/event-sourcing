using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Purview.EventSourcing.Aggregates;

namespace Purview.EventSourcing;

/// <summary>
/// Extensions for <see cref="IAggregate"/> and <see cref="AggregateBase"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[System.Diagnostics.DebuggerStepThrough]
public static class IAggregateExtensions
{
	/// <summary>
	/// Helper method that gets the <see cref="AggregateDetails.Id"/> from
	/// the <see cref="IAggregate.Details" /> property.
	/// </summary>
	/// <param name="aggregate">The <see cref="IAggregate"/>.</param>
	/// <returns>The <see cref="AggregateDetails.Id"/> from the <see cref="IAggregate.Details"/> property.</returns>
	public static string Id([NotNull] this IAggregate aggregate) => aggregate.Details.Id;

	/// <summary>
	/// Determines if the <see cref="IAggregate"/> instance is new,
	/// i.e. unsaved.
	/// </summary>
	/// <param name="aggregate">The <see cref="IAggregate"/> to check.</param>
	/// <returns>Returns true if the <see cref="AggregateDetails.SavedVersion"/> is 0, zero. Otherwise, returns false.</returns>
	public static bool IsNew([NotNull] this IAggregate aggregate) => aggregate.Details.SavedVersion == 0;

	/// <summary>
	/// <para>
	/// Similar to <see cref="AggregateBase.RecordAndApply{TEvent}(TEvent)"/> using property expressions and the <see cref="EqualityComparer{T}.Default"/>
	/// to see if an event should be raised.
	/// </para>
	/// <para>
	/// If the equality comparison fails, the <paramref name="proposedValue"/> is adapted via the
	/// <see cref="EventStoreOperationContext.ValueAdapter"/> (if it's not null) and then the <typeparamref name="TEvent"/> is
	/// created via the <paramref name="eventCreator"/>.
	/// </para>
	/// <para>Finally the resulting event is applied via <see cref="AggregateBase.RecordAndApply{TEvent}(TEvent)"/>.</para>
	/// </summary>
	/// <typeparam name="TAggregate">The <see cref="AggregateBase"/> type.</typeparam>
	/// <typeparam name="TProperty">The type of the property on the <typeparamref name="TAggregate"/>.</typeparam>
	/// <typeparam name="TEvent">The type of event to create.</typeparam>
	/// <param name="aggregate">The <typeparamref name="TAggregate"/> instance.</param>
	/// <param name="aggregatePropertyExpression">An expression to the property to update.</param>
	/// <param name="proposedValue">The new value of the property.</param>
	/// <param name="eventCreator">Creates a new <typeparamref name="TEvent"/> with the <paramref name="proposedValue"/>.</param>
	/// <returns>The <paramref name="aggregate"/>.</returns>
	public static TAggregate CompareRecordAndApply<TAggregate, TProperty, TEvent>(
		[NotNull] this TAggregate aggregate,
		[NotNull] Expression<Func<TAggregate, TProperty>> aggregatePropertyExpression,
		TProperty proposedValue,
		[NotNull] Func<TProperty, TEvent> eventCreator
	)
		where TAggregate : AggregateBase
		where TEvent : class
	{
		if (aggregatePropertyExpression.Body is not MemberExpression aggregateExpressionMember)
			throw new ArgumentException("Invalid property expression.", nameof(aggregatePropertyExpression));

		var existingValue = aggregatePropertyExpression.Compile()(aggregate);
		if (!EqualityComparer<TProperty>.Default.Equals(existingValue, proposedValue))
			aggregate.RecordAndApply(eventCreator(proposedValue));

		return aggregate;
	}
}
