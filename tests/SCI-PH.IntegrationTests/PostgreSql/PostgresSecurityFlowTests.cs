using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Accounts;
using SCI_PH.Application.Features.Authentication;
using SCI_PH.Application.Features.Devices;
using SCI_PH.IntegrationTests.Security;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.IntegrationTests.PostgreSql
{
    // The real services over real PostgreSQL: concurrency tokens, transactions and constraints behave as in production.
    [Collection(PostgresCollection.Name)]
    public sealed class PostgresSecurityFlowTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private readonly PostgresFixture _postgres;

        public PostgresSecurityFlowTests(PostgresFixture postgres) => _postgres = postgres;

        [SkippableFact]
        public async Task LoginRefreshRotationAndReuseDetection()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var context = new SecurityTestContext(postgresConnectionString: await _postgres.CreateMigratedDatabaseAsync());
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);

            var login = (await context.LoginAsync(user.Email, user.Password))!;
            var refreshed = (await context.RefreshAsync(login.RefreshToken))!;

            Assert.Null(await context.RefreshAsync(login.RefreshToken));
            Assert.Null(await context.RefreshAsync(refreshed.RefreshToken));
        }

        [SkippableFact]
        public async Task ConcurrentRefreshesOfTheSameTokenProduceASingleSession()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var context = new SecurityTestContext(postgresConnectionString: await _postgres.CreateMigratedDatabaseAsync());
            var user = await context.CreateUserAsync(SecurityRoles.Nurse);
            var login = (await context.LoginAsync(user.Email, user.Password))!;

            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => context.RefreshAsync(login.RefreshToken)));

            Assert.True(results.Count(r => r is not null) <= 1);
            Assert.True(await context.DbAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)) <= 1);
        }

        // Two tabs, same cookie, both read it as active: every round has one winner, the rest lose the compare-and-set
        // harmlessly. Atomic SaveChanges: the losers store no replacement, nothing is revoked, the winner's chain continues.
        [SkippableFact]
        public async Task ALostRotationRaceRevokesNothingAndStoresNoReplacement()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var barrier = new RefreshReadBarrier();
            await using var context = new SecurityTestContext(
                postgresConnectionString: await _postgres.CreateMigratedDatabaseAsync(), refreshReadBarrier: barrier);
            var user = await context.CreateUserAsync(SecurityRoles.Nurse);
            var current = (await context.LoginAsync(user.Email, user.Password))!.RefreshToken;

            for (var round = 0; round < 5; round++)
            {
                barrier.Arm(3);
                var token = current;
                var outcomes = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => context.RefreshSessionAsync(token))));

                var winner = Assert.Single(outcomes, o => o.Status == RefreshStatus.Succeeded);
                Assert.Equal(2, outcomes.Count(o => o.Status == RefreshStatus.LostConcurrencyRace));
                Assert.Equal(1, await context.DbAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)));
                current = winner.Session!.RefreshToken;
            }

            Assert.Equal(6, await context.DbAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == user.Id)));
            Assert.False(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.UserId == user.Id && e.EventType == SecurityAuditEventType.RefreshTokenReuseDetected)));
            Assert.Equal(RefreshStatus.Succeeded, (await context.RefreshSessionAsync(current)).Status);
        }

        // A replay after a committed rotation is still reuse on the real database: family revoked, descendant dead.
        [SkippableFact]
        public async Task ReplayAfterACommittedRotationStillRevokesTheFamily()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var context = new SecurityTestContext(postgresConnectionString: await _postgres.CreateMigratedDatabaseAsync());
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var t1 = (await context.LoginAsync(user.Email, user.Password))!.RefreshToken;
            var t2 = (await context.RefreshSessionAsync(t1)).Session!.RefreshToken;

            Assert.Equal(RefreshStatus.Failed, (await context.RefreshSessionAsync(t1)).Status);

            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.UserId == user.Id && e.EventType == SecurityAuditEventType.RefreshTokenReuseDetected)));
            Assert.False(await context.DbAsync(db => db.RefreshTokens.AnyAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)));
            Assert.Equal(RefreshStatus.Failed, (await context.RefreshSessionAsync(t2)).Status);
        }

        [SkippableFact]
        public async Task TwoConcurrentVerificationsOfTheSameMfaChallengeYieldExactlyOneSession()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var context = new SecurityTestContext(postgresConnectionString: await _postgres.CreateMigratedDatabaseAsync());
            var (user, key, _) = await MfaChallengeReplayTests.UserWithMfaAsync(context);

            // Several rounds so the race is really exercised; every round must give exactly one winner.
            for (var round = 0; round < 5; round++)
            {
                var challenge = await MfaChallengeReplayTests.ChallengeAsync(context, user);
                var code = Totp.Code(key);
                using var start = new ManualResetEventSlim();
                var requests = Enumerable.Range(0, 2)
                    .Select(_ => Task.Run(async () =>
                    {
                        start.Wait();
                        return await MfaChallengeReplayTests.Verify(context, challenge, code: code);
                    }))
                    .ToArray();
                start.Set();
                var results = await Task.WhenAll(requests);

                Assert.Equal(1, results.Count(r => r is not null));
                Assert.Equal(1, results.Count(r => r is null));
            }

            Assert.Equal(5, await MfaChallengeReplayTests.ActiveRefreshTokensAsync(context, user.Id));
        }

        [SkippableFact]
        public async Task ConcurrentRecoveryCodeVerificationsOfTheSameChallengeYieldExactlyOneSession()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var context = new SecurityTestContext(postgresConnectionString: await _postgres.CreateMigratedDatabaseAsync());
            var (user, _, codes) = await MfaChallengeReplayTests.UserWithMfaAsync(context);
            var challenge = await MfaChallengeReplayTests.ChallengeAsync(context, user);

            var results = await Task.WhenAll(
                Task.Run(() => MfaChallengeReplayTests.Verify(context, challenge, recoveryCode: codes[0])),
                Task.Run(() => MfaChallengeReplayTests.Verify(context, challenge, recoveryCode: codes[1])));

            Assert.Equal(1, results.Count(r => r is not null));
            Assert.Equal(1, await MfaChallengeReplayTests.ActiveRefreshTokensAsync(context, user.Id));
            // Only the winner redeemed a recovery code; the loser never reached code verification.
            Assert.Equal(9, await context.With<UserManager<ApplicationUser>, int>(async users =>
                await users.CountRecoveryCodesAsync((await users.FindByIdAsync(user.Id.ToString()))!)));
        }

        [SkippableFact]
        public async Task MfaChallengeIsPersistedHashedConsumedAndRejectedOnReplay()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var context = new SecurityTestContext(postgresConnectionString: await _postgres.CreateMigratedDatabaseAsync());
            var (user, key, _) = await MfaChallengeReplayTests.UserWithMfaAsync(context);
            var challenge = await MfaChallengeReplayTests.ChallengeAsync(context, user);
            var jti = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(challenge).Id;

            var pending = await context.DbAsync(db => db.MfaLoginChallenges.SingleAsync(c => c.UserId == user.Id));
            Assert.Null(pending.ConsumedAtUtc);
            Assert.NotEqual(jti, pending.ChallengeHash);
            Assert.InRange((pending.ExpiresAtUtc - pending.CreatedAtUtc).TotalMinutes, 4.9, 5.1);
            Assert.Null(await MfaChallengeReplayTests.Verify(context, challenge, code: "000000"));
            Assert.NotNull(await MfaChallengeReplayTests.Verify(context, challenge, code: Totp.Code(key)));
            Assert.Null(await MfaChallengeReplayTests.Verify(context, challenge, code: Totp.Code(key, stepOffset: -1)));
            Assert.NotNull((await context.DbAsync(db => db.MfaLoginChallenges.SingleAsync(c => c.UserId == user.Id))).ConsumedAtUtc);
            Assert.Equal(1, await MfaChallengeReplayTests.ActiveRefreshTokensAsync(context, user.Id));
        }

        [SkippableFact]
        public async Task InvitationActivationLinksThePatientAcrossSchemas()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var context = new SecurityTestContext(postgresConnectionString: await _postgres.CreateMigratedDatabaseAsync());
            await context.SeedAsync();
            var email = $"patient-{Guid.NewGuid():N}@sci-ph.test";
            var patientId = await context.CreatePatientAsync(email);

            await context.With<AccountInvitationService, InvitationResult>(s => s.InvitePatientAsync(patientId, Guid.NewGuid(), ClientContext.Unknown, None));
            var token = FakeEmailSender.LinkParameter(context.Email.LastTo(email), "token");
            var password = TestSettings.NewPassword();
            var activation = await context.With<AccountInvitationService, AccountActivationResult>(s =>
                s.ActivateAsync(new ActivateAccountRequest(token, password), ClientContext.Unknown, None));

            Assert.True(activation.Succeeded);
            var login = (await context.LoginAsync(email, password))!;
            Assert.Equal(login.User.Id, await context.ClinicalAsync(db => db.Patients.Where(p => p.Id == patientId).Select(p => p.IdentityUserId).SingleAsync()));
        }

        [SkippableFact]
        public async Task MfaPasswordResetAndDeviceCredentialsWork()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var context = new SecurityTestContext(postgresConnectionString: await _postgres.CreateMigratedDatabaseAsync());
            var user = await context.CreateUserAsync(SecurityRoles.Administrator);

            var setup = (await context.With<MfaManagementService, MfaSetup?>(s => s.BeginSetupAsync(user.Id, ClientContext.Unknown, None)))!;
            Assert.NotNull(await context.With<MfaManagementService, RecoveryCodes?>(s => s.EnableAsync(user.Id, Totp.Code(setup.SharedKey), ClientContext.Unknown, None)));
            var challenge = (await context.LoginRawAsync(user.Email, user.Password))!.Challenge!;
            Assert.NotNull(await context.AuthenticationAsync(a => a.VerifyMfaAsync(new MfaVerifyRequest(challenge.Token, Totp.Code(setup.SharedKey), null), ClientContext.Unknown, None)));

            await context.With<AccountRecoveryService>(s => s.RequestPasswordResetAsync(user.Email, ClientContext.Unknown, None));
            var message = context.Email.LastTo(user.Email);
            var reset = await context.With<AccountRecoveryService, PasswordChangeResult>(s => s.ResetPasswordAsync(
                new ResetPasswordRequest(user.Id, FakeEmailSender.LinkParameter(message, "token"), TestSettings.NewPassword()), ClientContext.Unknown, None));
            Assert.True(reset.Succeeded);

            var device = await context.With<DeviceCredentialService, DeviceSecretResult>(s =>
                s.ProvisionAsync(new ProvisionDeviceRequest("esp32-pg", "Unit", [DeviceScopes.Heartbeat]), user.Id, ClientContext.Unknown, None));
            var identity = await context.With<DeviceAuthenticationService, DeviceIdentity?>(s =>
                s.AuthenticateAsync("ESP32-PG", device.Secret!.Secret, ClientContext.Unknown, None));
            Assert.Equal([DeviceScopes.Heartbeat], identity!.Scopes);
            Assert.Equal(DeviceOperationOutcome.DuplicateCode, (await context.With<DeviceCredentialService, DeviceSecretResult>(s =>
                s.ProvisionAsync(new ProvisionDeviceRequest("ESP32-PG", "Unit", [DeviceScopes.Heartbeat]), user.Id, ClientContext.Unknown, None))).Outcome);
            Assert.NotEqual(device.Secret!.Secret, await context.DbAsync(db => db.DeviceCredentials.Select(d => d.SecretHash).SingleAsync()));
            Assert.True(await context.DbAsync(db => db.DataProtectionKeys.AnyAsync()));
        }
    }
}
