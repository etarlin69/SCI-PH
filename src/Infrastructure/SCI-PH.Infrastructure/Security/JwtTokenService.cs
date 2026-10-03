using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Infrastructure.Security
{
    // Issues HS256 access tokens and opaque refresh tokens. Works on UserAccount, so it needs no Persistence types.
    public sealed class JwtTokenService : ITokenService
    {
        // 512 bits of entropy for each refresh token.
        private const int RefreshTokenBytes = 64;

        private readonly JwtOptions _options;
        private readonly TimeProvider _time;
        private readonly SigningCredentials _credentials;
        private readonly TokenValidationParameters _challengeValidation;
        private readonly JsonWebTokenHandler _handler = new();

        public JwtTokenService(IOptions<JwtOptions> options, TimeProvider time)
        {
            _options = options.Value;
            _time = time;
            _credentials = new SigningCredentials(CreateSigningKey(_options), SecurityAlgorithms.HmacSha256);
            _challengeValidation = CreateValidationParameters(_options);
            _challengeValidation.LifetimeValidator = (notBefore, expires, _, parameters) =>
                IsWithinLifetime(notBefore, expires, parameters.ClockSkew);
        }

        // Only account identity, roles and permissions: never clinical or personal record data.
        public AccessToken CreateAccessToken(UserAccount account)
        {
            var now = _time.GetUtcNow();
            var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                Subject = new ClaimsIdentity(CreateClaims(account)),
                IssuedAt = now.UtcDateTime,
                NotBefore = now.UtcDateTime,
                Expires = expiresAt.UtcDateTime,
                SigningCredentials = _credentials
            };

            return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
        }

        public GeneratedRefreshToken CreateRefreshToken()
        {
            var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
            return new GeneratedRefreshToken(value, HashRefreshToken(value), _time.GetUtcNow().AddDays(_options.RefreshTokenLifetimeDays));
        }

        public string HashRefreshToken(string refreshToken) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

        // Same signing key, different token_use: JwtBearer rejects it on every normal endpoint.
        public MfaChallenge CreateMfaChallenge(Guid userId, string challengeId)
        {
            var now = _time.GetUtcNow();
            var expiresAt = now.AddMinutes(_options.MfaChallengeLifetimeMinutes);
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = _options.Issuer,
                Audience = _options.Audience,
                Subject = new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                    new Claim(JwtRegisteredClaimNames.Jti, challengeId),
                    new Claim(CustomClaimTypes.TokenUse, TokenUses.MfaChallenge)
                ]),
                IssuedAt = now.UtcDateTime,
                NotBefore = now.UtcDateTime,
                Expires = expiresAt.UtcDateTime,
                SigningCredentials = _credentials
            };

            return new MfaChallenge(_handler.CreateToken(descriptor), expiresAt);
        }

        public async Task<MfaChallengeClaims?> ReadMfaChallengeAsync(string challengeToken)
        {
            var result = await _handler.ValidateTokenAsync(challengeToken, _challengeValidation);
            if (!result.IsValid
                || !result.Claims.TryGetValue(CustomClaimTypes.TokenUse, out var use)
                || use as string != TokenUses.MfaChallenge
                || !result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var subject)
                || !Guid.TryParse(subject as string, out var userId)
                || !result.Claims.TryGetValue(JwtRegisteredClaimNames.Jti, out var jti)
                || jti is not string challengeId
                || string.IsNullOrEmpty(challengeId))
            {
                return null;
            }

            return new MfaChallengeClaims(userId, challengeId);
        }

        public static TokenValidationParameters CreateValidationParameters(JwtOptions options) =>
            new()
            {
                ValidIssuer = options.Issuer,
                ValidAudience = options.Audience,
                IssuerSigningKey = CreateSigningKey(options),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                NameClaimType = CustomClaimTypes.UserName,
                RoleClaimType = CustomClaimTypes.Role
            };

        private static IEnumerable<Claim> CreateClaims(UserAccount account)
        {
            yield return new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString());
            yield return new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString());
            yield return new Claim(CustomClaimTypes.TokenUse, TokenUses.Access);
            yield return new Claim(CustomClaimTypes.UserName, account.UserName);

            if (!string.IsNullOrEmpty(account.Email))
            {
                yield return new Claim(JwtRegisteredClaimNames.Email, account.Email);
            }

            foreach (var role in account.Roles)
            {
                yield return new Claim(CustomClaimTypes.Role, role);
            }

            foreach (var permission in account.Permissions)
            {
                yield return new Claim(CustomClaimTypes.Permission, permission);
            }
        }

        private bool IsWithinLifetime(DateTime? notBefore, DateTime? expires, TimeSpan clockSkew)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            return expires is not null && now <= expires.Value + clockSkew && (notBefore is null || now >= notBefore.Value - clockSkew);
        }

        private static SymmetricSecurityKey CreateSigningKey(JwtOptions options) =>
            new(Encoding.UTF8.GetBytes(options.SigningKey));
    }
}
