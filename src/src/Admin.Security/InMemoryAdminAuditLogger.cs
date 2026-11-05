using System.Collections.Concurrent;
using Purview.EventSourcing.Admin.Models;
using Purview.EventSourcing.Admin.Services;

namespace Purview.EventSourcing.Admin.Security;

/// <summary>
/// In-memory <see cref="IAdminAuditLogger"/> that retains the most recent audit entries for
/// inspection. Replace it with a durable implementation in production.
/// </summary>
public sealed class InMemoryAdminAuditLogger : IAdminAuditLogger
{
	readonly ConcurrentQueue<AdminAuditEntry> _entries = new();

	/// <summary>Returns the recorded entries, most recent first.</summary>
	public IReadOnlyList<AdminAuditEntry> Entries => [.. _entries];

	/// <inheritdoc/>
	public Task LogAsync(AdminAuditEntry entry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entry);
		_entries.Enqueue(entry);
		return Task.CompletedTask;
	}
}
