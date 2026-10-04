using System.ComponentModel.DataAnnotations;

namespace SCI_PH.Application.DTOs.Security
{
    public sealed record ChangePasswordRequest(
        [Required, MaxLength(256)] string CurrentPassword,
        [Required, MaxLength(256)] string NewPassword);
}
