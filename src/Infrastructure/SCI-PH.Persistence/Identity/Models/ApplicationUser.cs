using Microsoft.AspNetCore.Identity;

namespace SCI_PH.Persistence.Identity.Models
{
    // Access account only. It is not a Patient nor a ClinicalStaff member and holds no clinical data;
    // those records reference the account through an optional IdentityUserId without a foreign key.
    public class ApplicationUser : IdentityUser<Guid>
    {
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset? LastLoginAtUtc { get; private set; }
        public bool IsActive { get; private set; } = true;
        public DateTimeOffset? DeactivatedAtUtc { get; private set; }

        public static ApplicationUser Create(string userName, string? email, DateTimeOffset createdAtUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                Email = email,
                CreatedAtUtc = createdAtUtc.ToUniversalTime()
            };

        public void RecordLogin(DateTimeOffset loggedInAtUtc) => LastLoginAtUtc = loggedInAtUtc.ToUniversalTime();

        // Deactivation keeps the account and its history; it only blocks new sessions.
        public void Deactivate(DateTimeOffset deactivatedAtUtc)
        {
            IsActive = false;
            DeactivatedAtUtc = deactivatedAtUtc.ToUniversalTime();
        }

        public void Activate()
        {
            IsActive = true;
            DeactivatedAtUtc = null;
        }
    }
}
