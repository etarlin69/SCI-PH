namespace SCI_PH.Persistence.Identity.Seed
{
    // Opt-in bootstrap. AdminEmail and AdminPassword come only from User Secrets, environment
    // variables or a vault (IdentityBootstrap__AdminPassword); never from appsettings or the repository.
    public sealed class IdentityBootstrapOptions
    {
        public const string SectionName = "IdentityBootstrap";

        public bool Enabled { get; init; }
        public string? AdminEmail { get; init; }
        public string? AdminPassword { get; init; }

        public bool HasAdministrator => !string.IsNullOrWhiteSpace(AdminEmail) && !string.IsNullOrEmpty(AdminPassword);
    }
}
