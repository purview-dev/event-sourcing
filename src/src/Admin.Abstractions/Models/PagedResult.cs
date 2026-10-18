namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// Represents a page of items returned by a paged query.
/// </summary>
/// <typeparam name="T">The type of item contained in the page.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">The maximum number of items per page.</param>
/// <param name="TotalCount">The total number of items across all pages.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
	/// <summary>
	/// Gets the total number of pages based on <see cref="TotalCount"/> and <see cref="PageSize"/>.
	/// </summary>
	public int TotalPages => (int)((TotalCount + PageSize - 1) / PageSize);

	/// <summary>
	/// Gets a value indicating whether an additional page of results is available.
	/// </summary>
	public bool HasNextPage => Page < TotalPages;

	/// <summary>
	/// Gets a value indicating whether a previous page of results is available.
	/// </summary>
	public bool HasPreviousPage => Page > 1;
}
