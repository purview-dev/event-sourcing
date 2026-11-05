using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Purview.EventSourcing.EntityFrameworkCore.Mapping;

/// <summary>
/// Implemented by a snapshot <see cref="DbContext"/> whose model varies with its options - the aggregate
/// being mapped, and the table or container it is stored in.
/// </summary>
interface ISnapshotModelCacheKeySource
{
	/// <summary>
	/// Creates the key identifying this context's model.
	/// </summary>
	/// <param name="designTime">Whether the model is being built for design-time use.</param>
	/// <returns>The model cache key.</returns>
	object CreateModelCacheKey(bool designTime);
}

/// <summary>
/// Keys the EF model cache on the snapshot context's own identity, so two contexts that map different
/// aggregates - or the same aggregate into a different table or container - do not share a model.
/// </summary>
sealed class SnapshotModelCacheKeyFactory : IModelCacheKeyFactory
{
	/// <summary>
	/// Creates the model cache key for <paramref name="context"/>.
	/// </summary>
	/// <param name="context">The context.</param>
	/// <param name="designTime">Whether the model is being built for design-time use.</param>
	/// <returns>The model cache key.</returns>
	public object Create(DbContext context, bool designTime) =>
		context is ISnapshotModelCacheKeySource source
			? source.CreateModelCacheKey(designTime)
			: (context?.GetType(), designTime);
}
