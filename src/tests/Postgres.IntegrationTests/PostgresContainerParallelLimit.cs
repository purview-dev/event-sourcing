using TUnit.Core.Interfaces;

// The suite shares one PostgreSQL container, and its connection limit - not the CPU count - is what
// bounds safe concurrency here. Unbounded parallelism exhausts the pool and fails with connection
// timeouts, so the limit travels with the suite rather than depending on the caller passing
// --maximum-parallel-tests.
[assembly: ParallelLimiter<PostgresContainerParallelLimit>]

namespace Purview.EventSourcing.Postgres;

/// <summary>
/// Caps concurrent tests to a level the shared PostgreSQL container's connection pool sustains.
/// </summary>
public sealed class PostgresContainerParallelLimit : IParallelLimit
{
	/// <inheritdoc/>
	public int Limit => 4;
}
