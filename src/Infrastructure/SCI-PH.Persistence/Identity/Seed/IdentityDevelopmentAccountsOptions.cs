namespace SCI_PH.Persistence.Identity.Seed
{
    // Development only (the host must be in the Development environment AND Enabled must be true). One local test account
    // per listed role, so the team can sign in without invitations. Password comes only from User Secrets or environment
    // variables (IdentityDevelopmentAccounts__Password); never from appsettings or the repository. Administrator is not
    // allowed here: the first administrator comes from IdentityBootstrap.
    public sealed class IdentityDevelopmentAccountsOptions
    {
        public const string SectionName = "IdentityDevelopmentAccounts";

        public bool Enabled { get; init; }
        // Shared by every development account. It must satisfy the password policy.
        public string? Password { get; init; }
        public List<IdentityDevelopmentAccount> Accounts { get; init; } = [];
    }

    public sealed class IdentityDevelopmentAccount
    {
        public string Email { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
    }
}
