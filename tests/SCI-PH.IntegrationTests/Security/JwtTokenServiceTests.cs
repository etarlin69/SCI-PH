using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Infrastructure.Security;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Security
{
    public class JwtTokenServiceTests
    {
        private static readonly JwtOptions Options = new()
        {
            Issuer = TestSettings.Issuer,
            Audience = TestSettings.Audience,
            SigningKey = TestSettings.SigningKey,
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7
        };

        private static readonly UserAccount Account = new(
            Guid.NewGuid(),
            "dr.house",
            "house@sci-ph.test",
            [SecurityRoles.Doctor],
            RolePermissions.For(SecurityRoles.Doctor));

        private static JwtTokenService CreateService(FakeTimeProvider time, JwtOptions? options = null) =>
            new(Microsoft.Extensions.Options.Options.Create(options ?? Options), time);

        [Fact]
        public async Task AccessTokenHasTheExpectedClaimsAndLifetime()
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var token = CreateService(time).CreateAccessToken(Account);

            var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, JwtTokenService.CreateValidationParameters(Options));

            Assert.True(validation.IsValid, validation.Exception?.Message);
            var jwt = (JsonWebToken)validation.SecurityToken;
            Assert.Equal(TestSettings.Issuer, jwt.Issuer);
            Assert.Equal([TestSettings.Audience], jwt.Audiences);
            Assert.Equal(Account.Id.ToString(), jwt.Subject);
            Assert.False(string.IsNullOrEmpty(jwt.Id));
            Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Alg);
            Assert.Equal(time.GetUtcNow().AddMinutes(15), token.ExpiresAtUtc);
            Assert.Equal(token.ExpiresAtUtc.ToUnixTimeSeconds(), new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds());
            Assert.Equal(Account.Roles, jwt.Claims.Where(c => c.Type == CustomClaimTypes.Role).Select(c => c.Value));
            Assert.Equal(Account.Permissions, jwt.Claims.Where(c => c.Type == CustomClaimTypes.Permission).Select(c => c.Value));
        }

        [Fact]
        public void TokensHaveUniqueIds()
        {
            var service = CreateService(new FakeTimeProvider(DateTimeOffset.UtcNow));

            var first = new JsonWebToken(service.CreateAccessToken(Account).Value);
            var second = new JsonWebToken(service.CreateAccessToken(Account).Value);

            Assert.NotEqual(first.Id, second.Id);
        }

        [Fact]
        public void AccessTokenCarriesOnlyAccountClaims()
        {
            var jwt = new JsonWebToken(CreateService(new FakeTimeProvider(DateTimeOffset.UtcNow)).CreateAccessToken(Account).Value);
            string[] allowed = ["sub", "jti", "token_use", "name", "email", "role", "permission", "iss", "aud", "exp", "nbf", "iat"];

            Assert.All(jwt.Claims, claim => Assert.Contains(claim.Type, allowed));
        }

        [Fact]
        public async Task TokenSignedWithAnotherKeyOrExpiredIsRejected()
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var otherKey = new JwtOptions
            {
                Issuer = Options.Issuer,
                Audience = Options.Audience,
                SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            };
            var forged = CreateService(time, otherKey).CreateAccessToken(Account).Value;
            var expired = CreateService(new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-2))).CreateAccessToken(Account).Value;
            var handler = new JsonWebTokenHandler();
            var parameters = JwtTokenService.CreateValidationParameters(Options);

            Assert.False((await handler.ValidateTokenAsync(forged, parameters)).IsValid);
            Assert.False((await handler.ValidateTokenAsync(expired, parameters)).IsValid);
        }

        [Fact]
        public void RefreshTokensAreRandomAndOnlyTheirHashIsDeterministic()
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var service = CreateService(time);

            var first = service.CreateRefreshToken();
            var second = service.CreateRefreshToken();

            Assert.NotEqual(first.Value, second.Value);
            Assert.True(first.Value.Length >= 80);
            Assert.Equal(first.Hash, service.HashRefreshToken(first.Value));
            Assert.Matches("^[0-9a-f]{64}$", first.Hash);
            Assert.Equal(time.GetUtcNow().AddDays(7), first.ExpiresAtUtc);
        }

        [Theory]
        [InlineData("", "aud", 15, 7)]
        [InlineData("iss", "", 15, 7)]
        [InlineData("iss", "aud", 0, 7)]
        [InlineData("iss", "aud", 24 * 60, 7)]
        [InlineData("iss", "aud", 15, 365)]
        public void UnsafeOptionsAreRejected(string issuer, string audience, int accessMinutes, int refreshDays)
        {
            var options = new JwtOptions
            {
                Issuer = issuer,
                Audience = audience,
                SigningKey = TestSettings.SigningKey,
                AccessTokenLifetimeMinutes = accessMinutes,
                RefreshTokenLifetimeDays = refreshDays
            };

            Assert.True(new JwtOptionsValidator().Validate(null, options).Failed);
        }

        [Fact]
        public void MissingOrShortSigningKeyIsRejectedWithoutEchoingIt()
        {
            var shortKey = "too-short-key";
            var result = new JwtOptionsValidator().Validate(null, new JwtOptions { Issuer = "iss", Audience = "aud", SigningKey = shortKey });

            Assert.True(result.Failed);
            Assert.DoesNotContain(shortKey, result.FailureMessage);
            Assert.True(new JwtOptionsValidator().Validate(null, Options).Succeeded);
        }
    }
}
