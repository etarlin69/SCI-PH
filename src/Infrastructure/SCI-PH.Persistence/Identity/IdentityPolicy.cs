using Microsoft.AspNetCore.Identity;

namespace SCI_PH.Persistence.Identity
{
    // Account rules applied to ASP.NET Core Identity. See docs/adr/ADR-006-identity-security.md.
    public static class IdentityPolicy
    {
        public const int MinimumPasswordLength = 10;
        public const int MaxFailedAccessAttempts = 5;
        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        public static void Configure(IdentityOptions options)
        {
            options.User.RequireUniqueEmail = true;

            options.Password.RequiredLength = MinimumPasswordLength;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;

            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = MaxFailedAccessAttempts;
            options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

            // No email service exists yet; requiring confirmation now would make every account unusable.
            options.SignIn.RequireConfirmedEmail = false;
            options.SignIn.RequireConfirmedAccount = false;
        }
    }
}
