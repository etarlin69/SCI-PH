using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Services
{
    internal sealed class SecurityAuditLog : ISecurityAuditLog
    {
        private readonly IdentityDbContext _db;
        private readonly TimeProvider _time;

        public SecurityAuditLog(IdentityDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        public async Task RecordAsync(SecurityAuditRecord record, CancellationToken cancellationToken)
        {
            _db.SecurityAuditEvents.Add(SecurityAuditEvent.From(record, _time.GetUtcNow()));
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
