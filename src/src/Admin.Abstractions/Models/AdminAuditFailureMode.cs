namespace Purview.EventSourcing.Admin.Models;

/// <summary>
/// What happens when an audit entry cannot be written.
/// </summary>
/// <remarks>
/// Both answers are defensible and the choice belongs to the deployment, so it is explicit rather
/// than assumed. In a regulated deployment the audit trail <em>is</em> the control, and an operation
/// nobody can prove happened is worse than an operation that did not happen.
/// </remarks>
public enum AdminAuditFailureMode
{
	/// <summary>
	/// The operation proceeds and the audit failure is logged. Availability over evidence.
	/// </summary>
	Continue = 0,

	/// <summary>
	/// The operation is refused. Evidence over availability.
	/// </summary>
	Fail = 1,
}
