using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Features.Accounts
{
    // Plain-text templates. They contain links with one-time tokens, never passwords or account details.
    public static class AccountEmails
    {
        private const string Footer = "\n\nSi no esperabas este mensaje, ignóralo. SCI-PH nunca te pedirá tu contraseña por correo.";

        public static EmailMessage Invitation(string to, string link, DateTimeOffset expiresAtUtc) =>
            new(to, "SCI-PH: activa tu cuenta",
                "Se creó una invitación para tu cuenta de SCI-PH.\n\n"
                + $"Para activarla y elegir tu contraseña abre este enlace:\n{link}\n\n"
                + $"El enlace vence el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC y solo puede usarse una vez." + Footer);

        public static EmailMessage ConfirmEmail(string to, string link) =>
            new(to, "SCI-PH: confirma tu correo",
                $"Confirma tu dirección de correo abriendo este enlace:\n{link}" + Footer);

        public static EmailMessage PasswordReset(string to, string link) =>
            new(to, "SCI-PH: restablece tu contraseña",
                $"Recibimos una solicitud para restablecer tu contraseña. Abre este enlace para elegir una nueva:\n{link}\n\n"
                + "Al cambiarla se cerrarán todas tus sesiones abiertas." + Footer);
    }
}
