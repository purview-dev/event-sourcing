namespace Purview.EventSourcing.Faults;

/// <summary>
/// The store operations a fault can be injected into.
/// </summary>
public enum FaultOperation
{
	/// <summary>Reading an aggregate.</summary>
	Get = 0,

	/// <summary>Saving an aggregate.</summary>
	Save = 1,

	/// <summary>Deleting an aggregate.</summary>
	Delete = 2,

	/// <summary>Reading a range of events.</summary>
	GetEventRange = 3,
}

/// <summary>
/// How a fault behaves when it fires.
/// </summary>
public enum FaultKind
{
	/// <summary>The operation throws before reaching storage. Nothing was written.</summary>
	FailBefore = 0,

	/// <summary>
	/// The operation reaches storage and succeeds, then throws. Storage changed; the caller believes
	/// it did not.
	/// </summary>
	/// <remarks>
	/// This is the fault worth testing above all others. It is what a lost response, a timeout after
	/// commit, or a connection dropped mid-acknowledgement looks like to a client - and it is the one
	/// that produces duplicates, because the caller's natural reaction is to retry an operation that
	/// actually happened.
	/// </remarks>
	FailAfter = 1,
}

/// <summary>
/// Describes which store calls should fail, and how.
/// </summary>
/// <remarks>
/// Deliberately explicit rather than random: a test that fails the third save reproduces, where one
/// that fails a random save turns a real defect into an intermittent annoyance nobody trusts.
/// </remarks>
public sealed class FaultPlan
{
	readonly List<ScheduledFault> _faults = [];

	/// <summary>A plan that injects nothing.</summary>
	public static FaultPlan None => new();

	/// <summary>
	/// Fails the nth call to an operation before it reaches storage.
	/// </summary>
	/// <param name="operation">The operation to fail.</param>
	/// <param name="onCall">Which call to fail, counting from one.</param>
	/// <param name="exception">The exception to throw; defaults to an I/O failure.</param>
	public FaultPlan FailBefore(FaultOperation operation, int onCall = 1, Exception? exception = null)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(onCall, 1);
		_faults.Add(new(operation, FaultKind.FailBefore, onCall, exception));

		return this;
	}

	/// <summary>
	/// Lets the nth call to an operation reach storage, then fails it.
	/// </summary>
	/// <param name="operation">The operation to fail.</param>
	/// <param name="onCall">Which call to fail, counting from one.</param>
	/// <param name="exception">The exception to throw; defaults to an I/O failure.</param>
	/// <remarks>
	/// Models a lost response: the write happened, the caller was never told, and whatever it does
	/// next has to be safe.
	/// </remarks>
	public FaultPlan FailAfter(FaultOperation operation, int onCall = 1, Exception? exception = null)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(onCall, 1);
		_faults.Add(new(operation, FaultKind.FailAfter, onCall, exception));

		return this;
	}

	/// <summary>
	/// Returns the fault to apply for this call, if any, and records the call.
	/// </summary>
	/// <param name="operation">The operation being invoked.</param>
	internal ScheduledFault? NextFor(FaultOperation operation)
	{
		lock (_faults)
		{
			var callNumber = _callCounts.GetValueOrDefault(operation) + 1;
			_callCounts[operation] = callNumber;

			var match = _faults.Find(fault =>
				fault.Operation == operation && fault.OnCall == callNumber && !fault.Fired
			);

			if (match is null)
				return null;

			// Fired once: a fault that repeated would make a retry test impossible to write, because
			// the retry would fail too.
			match.Fired = true;

			return match;
		}
	}

	readonly Dictionary<FaultOperation, int> _callCounts = [];

	internal sealed record ScheduledFault(FaultOperation Operation, FaultKind Kind, int OnCall, Exception? Exception)
	{
		public bool Fired { get; set; }

		public Exception Build() => Exception ?? new IOException($"Injected {Kind} fault on {Operation}.");
	}
}
