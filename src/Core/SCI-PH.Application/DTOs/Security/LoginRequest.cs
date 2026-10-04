using System.ComponentModel.DataAnnotations;

namespace SCI_PH.Application.DTOs.Security
{
    public sealed record LoginRequest(
        [Required, MaxLength(256)] string Identifier,
        [Required, MaxLength(256)] string Password);
}
