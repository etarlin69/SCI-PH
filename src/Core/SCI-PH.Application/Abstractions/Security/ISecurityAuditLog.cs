using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    // Append-only security log, separate from the clinical audit.
    public interface ISecurityAuditLog
    {
        Task RecordAsync(SecurityAuditRecord record, CancellationToken cancellationToken);
    }
}
