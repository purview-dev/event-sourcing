using Purview.EventSourcing.Failures;
using Purview.Results;

namespace Purview.EventSourcing;

/// <summary>
/// Provides a retry-with-backoff policy for optimistic-concurrency write conflicts.
/// </summary>
/// <remarks>
/// <para>
/// The framework itself does not retry failed writes; a conflict is surfaced to the caller. Callers
/// that need a read-modify-write loop (load an aggregate, apply domain changes, save, and re-read +
/// re-apply on a conflict) should wrap their operation with one of the <c>ExecuteAsync</c> overloads.
/// </para>
/// <para>
/// Save operations report a conflict as a <see cref="ConcurrencyConflict"/> case, so use
/// <see cref="ExecuteAsync{TValue, TError}(Func{Task{Result{TValue, TError}}}, int, TimeSpan?, CancellationToken)"/>
/// for those. The exception-based overload remains for operations that still throw, and by default
/// retries exceptions implementing <see cref="IConcurrencyConflict"/>.
/// </para>
/// </remarks>
public static class ConcurrencyRetry
{
	/// <summary>
	/// Executes <paramref name="operation"/> retrying it with exponential backoff while it returns a
	/// <see cref="ConcurrencyConflict"/>. After <paramref name="maxAttempts"/> attempts the last
	/// result is returned as-is.
	/// </summary>
	/// <typeparam name="TValue">The result's value type.</typeparam>
	/// <typeparam name="TError">The result's error union.</typeparam>
	/// <param name="operation">The operation to execute, typically a load → modify → save loop.</param>
	/// <param name="maxAttempts">The maximum number of attempts (inclusive), at least 1.</param>
	/// <param name="initialBackoff">The base delay before the first retry; doubles each attempt.</param>
	/// <param name="cancellationToken">The stopping token, also cancels the backoff delay.</param>
	/// <returns>The first non-conflicting result, or the last conflicting one once the attempts run out.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is less than 1.</exception>
	public static async Task<Result<TValue, TError>> ExecuteAsync<TValue, TError>(
		Func<Task<Result<TValue, TError>>> operation,
		int maxAttempts = 3,
		TimeSpan? initialBackoff = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(operation);
		if (maxAttempts < 1)
			throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Must be at least 1.");

		var backoff = initialBackoff ?? TimeSpan.FromMilliseconds(50);

		for (var attempt = 1; ; attempt++)
		{
			var result = await operation().ConfigureAwait(false);
			if (result.IsSuccess || attempt >= maxAttempts || !IsConcurrencyConflict(result.Error))
				return result;

			await Task.Delay(backoff * Math.Pow(2, attempt - 1), cancellationToken).ConfigureAwait(false);
		}
	}

	// The error may be the case itself, or a union wrapping it, so the union's own members are checked
	// rather than assuming a particular union shape.
	static bool IsConcurrencyConflict<TError>(TError error) =>
		error switch
		{
			ConcurrencyConflict => true,
			AggregateSaveFailure save => save is ConcurrencyConflict,
			AggregateDeleteFailure delete => delete is ConcurrencyConflict,
			AggregateRestoreFailure restore => restore is ConcurrencyConflict,
			_ => false,
		};

	/// <summary>
	/// Executes <paramref name="operation"/> retrying it with exponential backoff when it throws a
	/// concurrency-conflict exception. After <paramref name="maxAttempts"/> attempts the last
	/// exception is rethrown.
	/// </summary>
	/// <typeparam name="TResult">The result type of the operation.</typeparam>
	/// <param name="operation">The operation to execute, typically a load → modify → save loop.</param>
	/// <param name="maxAttempts">The maximum number of attempts (inclusive), at least 1.</param>
	/// <param name="initialBackoff">The base delay before the first retry; doubles each attempt.</param>
	/// <param name="isConcurrencyException">
	/// Determines whether an exception is retryable. Defaults to testing for <see cref="IConcurrencyConflict"/>.
	/// </param>
	/// <param name="cancellationToken">The stopping token, also cancels the backoff delay.</param>
	/// <returns>The result of a successful attempt.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is less than 1.</exception>
	public static async Task<TResult> ExecuteAsync<TResult>(
		Func<Task<TResult>> operation,
		int maxAttempts = 3,
		TimeSpan? initialBackoff = null,
		Func<Exception, bool>? isConcurrencyException = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(operation);
		if (maxAttempts < 1)
			throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Must be at least 1.");

		isConcurrencyException ??= static ex => ex is IConcurrencyConflict;
		var backoff = initialBackoff ?? TimeSpan.FromMilliseconds(50);

		var attempt = 0;
		while (true)
		{
			attempt++;
			try
			{
				return await operation().ConfigureAwait(false);
			}
			catch (Exception ex) when (isConcurrencyException(ex) && attempt < maxAttempts)
			{
				var delay = backoff * Math.Pow(2, attempt - 1);
				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
			}
		}
	}
}
