using System.ComponentModel.DataAnnotations;

namespace SCI_PH.Application.DTOs.Security
{
    public sealed record RefreshTokenRequest([Required, MaxLength(512)] string RefreshToken);
}
