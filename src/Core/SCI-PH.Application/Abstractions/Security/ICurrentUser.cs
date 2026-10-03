namespace SCI_PH.Application.Abstractions.Security
{
    // Authenticated caller as seen by Application, without exposing HttpContext.
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }

        Guid? UserId { get; }

        IReadOnlyCollection<string> Roles { get; }

        IReadOnlyCollection<string> Permissions { get; }

        bool HasPermission(string permission);
    }
}
