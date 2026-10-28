using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Infrastructure.Biometrics.Face;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SkiaSharp;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Assisted 1:1 face verification over the real Web BFF and PostgreSQL with the deterministic provider (ADR-012):
    // enrollment, verification against the patient's own active reference only, revocation, idempotency, the single active
    // face per patient (also under concurrency, with the loser's provider subject compensated), the input policy, the 503
    // of a failing provider, and no subject reference, score, threshold or image anywhere outside the provider.
    [Collection(PostgresCollection.Name)]
    public sealed class FaceVerificationApiTests
    {
        private readonly PostgresFixture _postgres;

        public FaceVerificationApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static string Faces(Guid patientId) => $"/api/web/patients/{patientId}/faces";

        private static MultipartFormDataContent Form(byte[] capture, Guid? clientRequestId, string contentType = "image/png", Action<MultipartFormDataContent>? extra = null)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(capture);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "capture", "capture.png");
            if (clientRequestId is { } id)
            {
                form.Add(new StringContent(id.ToString()), "clientRequestId");
            }

            extra?.Invoke(form);
            return form;
        }

        private static Task<HttpResponseMessage> EnrollAsync(StaffSession staff, Guid patientId, byte[] capture, Guid? key = null) =>
            staff.Client.PostAsync($"{Faces(patientId)}/enroll", Form(capture, key ?? Guid.NewGuid()));

        private static Task<HttpResponseMessage> VerifyAsync(StaffSession staff, Guid patientId, byte[] capture, Guid? key = null) =>
            staff.Client.PostAsync($"{Faces(patientId)}/verify", Form(capture, key ?? Guid.NewGuid()));

        private static Task<HttpResponseMessage> RevokeAsync(StaffSession staff, Guid patientId, Guid referenceId, string json = """{"reason":"PatientRequest"}""") =>
            staff.Client.PostAsync($"{Faces(patientId)}/{referenceId}/revoke", new StringContent(json, Encoding.UTF8, "application/json"));

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        private static async Task<string> OutcomeAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await JsonAsync(response)).GetProperty("outcome").GetString()!;
        }

        private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            await Task.CompletedTask;
        }

        private static Task<List<PatientBiometricReference>> FacesAsync(ClinicalApiHost host, Guid patientId) =>
            host.DbAsync(db => db.PatientBiometricReferences.AsNoTracking()
                .Where(r => r.PatientId == patientId && r.Modality == BiometricModality.Face).ToListAsync());

        private static Task<int> AuditsAsync(ClinicalApiHost host, string entityName, AuditActionType? action = null) =>
            host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.EntityName == entityName && (action == null || a.Action == action)));

        private static FakeFaceVerificationService Provider(ClinicalApiHost host) => host.Services.GetRequiredService<FakeFaceVerificationService>();

        private sealed record Setup(ClinicalApiHost Host, Clinic Clinic, StaffSession Reception, Patient Patient);

        private async Task<Setup> SetupAsync()
        {
            var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var patient = await host.SeedPatientAsync();
            return new Setup(host, clinic, reception, patient);
        }

        // The whole lifecycle with the deterministic provider: enroll, verify (Match, NoMatch, the three quality outcomes,
        // a replay), revoke (and its replay), verify after revoke (409), enroll a replacement.
        [SkippableFact]
        public async Task AFaceIsEnrolledVerifiedOneToOneRevokedAndReplaced()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient) = await SetupAsync();
            await using var _host = host;
            Assert.Empty((await JsonAsync(await reception.Client.GetAsync(Faces(patient.Id)))).EnumerateArray());

            var enrolled = await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA));
            Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
            var enrolledRaw = await enrolled.Content.ReadAsStringAsync();
            var face = JsonDocument.Parse(enrolledRaw).RootElement.GetProperty("face");
            Assert.Equal("Enrolled", JsonDocument.Parse(enrolledRaw).RootElement.GetProperty("outcome").GetString());
            Assert.Equal(["id", "status", "provider", "registeredAtUtc", "revokedAtUtc"], face.EnumerateObject().Select(p => p.Name));
            Assert.Equal(("Active", "SCIPH-FaceFake"), (face.GetProperty("status").GetString(), face.GetProperty("provider").GetString()));
            var referenceId = face.GetProperty("id").GetGuid();
            var stored = Assert.Single(await FacesAsync(host, patient.Id));
            Assert.StartsWith("face:", stored.SubjectReference);
            Assert.DoesNotContain(patient.Id.ToString(), stored.SubjectReference);
            Assert.Equal(("SCIPH-FaceFake", "fake-v1", reception.StaffId, BiometricReferenceStatus.Active),
                (stored.Provider, stored.AlgorithmVersion, stored.RegisteredByStaffId, stored.Status));
            Assert.True(Provider(host).Holds(stored.SubjectReference));

            Assert.Equal("Match", await OutcomeAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA))));
            Assert.Equal("Match", await OutcomeAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Jpeg(FaceFixtures.FaceA))));
            Assert.Equal("NoMatch", await OutcomeAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB))));
            Assert.Equal("NoFace", await OutcomeAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.NoFace))));
            Assert.Equal("MultipleFaces", await OutcomeAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.MultipleFaces))));
            Assert.Equal("LowQuality", await OutcomeAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.LowQuality))));

            // Replay: the recorded outcome, no second audit entry; same key with another capture: 409.
            var key = Guid.NewGuid();
            var first = await JsonAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB), key));
            var verifications = await AuditsAsync(host, PatientFaceService.VerificationEntityName);
            var again = await JsonAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB), key));
            Assert.Equal(first.GetRawText(), again.GetRawText());
            Assert.Equal(["verificationId", "outcome"], first.EnumerateObject().Select(p => p.Name));
            Assert.Equal(verifications, await AuditsAsync(host, PatientFaceService.VerificationEntityName));
            await AssertProblemAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA), key), HttpStatusCode.Conflict);

            // A verification never identifies or changes anything clinical.
            Assert.Equal(0, await host.DbAsync(db => db.IdentificationAttempts.CountAsync()));
            Assert.Equal(0, await host.DbAsync(db => db.ClinicalEncounters.CountAsync()));
            Assert.Equal(7, verifications);

            var revoked = await RevokeAsync(reception, patient.Id, referenceId);
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
            var revokedRaw = await revoked.Content.ReadAsStringAsync();
            Assert.Equal("Revoked", JsonDocument.Parse(revokedRaw).RootElement.GetProperty("status").GetString());
            Assert.False(Provider(host).Holds(stored.SubjectReference));
            var replay = await RevokeAsync(reception, patient.Id, referenceId, """{"reason":"Other"}""");
            Assert.Equal((HttpStatusCode.OK, revokedRaw), (replay.StatusCode, await replay.Content.ReadAsStringAsync()));
            // The revocation and the completion of its template cleanup (one job, completed by the immediate attempt).
            Assert.Equal(2, await host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.EntityName == nameof(PatientBiometricReference) && a.Action == AuditActionType.Update)));
            var job = await host.DbAsync(db => db.FaceTemplateDeletionJobs.AsNoTracking().SingleAsync());
            Assert.Equal((referenceId, FaceTemplateDeletionStatus.Completed, FaceTemplateDeletionOutcome.Deleted),
                (job.BiometricReferenceId, job.Status, job.Outcome));
            await AssertProblemAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA)), HttpStatusCode.Conflict);

            Assert.Equal(HttpStatusCode.Created, (await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB))).StatusCode);
            var references = await FacesAsync(host, patient.Id);
            Assert.Equal((1, 1), (references.Count(r => r.Status == BiometricReferenceStatus.Active), references.Count(r => r.Status == BiometricReferenceStatus.Revoked)));
            Assert.Equal("Match", await OutcomeAsync(await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB))));
            Assert.Equal(1, Provider(host).Count);
        }

        // Quality outcomes create nothing; a retried enrollment is the same reference without calling the provider again;
        // the same key with another capture is 409; a second active face is 409 before the provider is called.
        [SkippableFact]
        public async Task EnrollmentIsIdempotentAndOneFaceIsActivePerPatient()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient) = await SetupAsync();
            await using var _host = host;

            foreach (var (colour, outcome) in new[] { (FaceFixtures.NoFace, "NoFace"), (FaceFixtures.MultipleFaces, "MultipleFaces"), (FaceFixtures.LowQuality, "LowQuality") })
            {
                var response = await EnrollAsync(reception, patient.Id, FaceFixtures.Png(colour));
                Assert.Equal(outcome, await OutcomeAsync(response));
            }

            Assert.Empty(await FacesAsync(host, patient.Id));
            Assert.Equal(0, Provider(host).Count);

            var key = Guid.NewGuid();
            var created = await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA), key);
            var retried = await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA), key);
            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (created.StatusCode, retried.StatusCode));
            Assert.Equal((await JsonAsync(created)).GetRawText(), (await JsonAsync(retried)).GetRawText());
            await AssertProblemAsync(await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB), key), HttpStatusCode.Conflict);
            await AssertProblemAsync(await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB)), HttpStatusCode.Conflict);
            Assert.Single(await FacesAsync(host, patient.Id));
            Assert.Equal(1, Provider(host).Count);
            Assert.Equal(1, await host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.EntityName == nameof(PatientBiometricReference) && a.Action == AuditActionType.Create)));
        }

        // D40 (deterministic race): another transaction holds an uncommitted ACTIVE face of the patient; two enrollments
        // pass the pre-check, enroll on the provider and both wait on the unique index. When it rolls back, one commits (201)
        // and the other loses on UX_PatientBiometricReferences_ActiveFacePerPatient (409) and deletes its provider subject.
        [SkippableFact]
        public async Task ConcurrentEnrollmentsLeaveOneActiveFaceAndCompensateTheLoser()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient) = await SetupAsync();
            await using var _host = host;

            await using var connection = new NpgsqlConnection(host.ConnectionString);
            await connection.OpenAsync();
            var hold = await connection.BeginTransactionAsync();
            await using (var insert = new NpgsqlCommand(
                """INSERT INTO clinical."PatientBiometricReferences" ("Id", "PatientId", "Modality", "Provider", "SubjectReference", "Status", "CreatedAtUtc") VALUES (@id, @patient, 2, 'SCIPH-FaceFake', @subject, 1, now())""",
                connection, hold))
            {
                insert.Parameters.AddWithValue("id", Guid.NewGuid());
                insert.Parameters.AddWithValue("patient", patient.Id);
                insert.Parameters.AddWithValue("subject", $"face:{Guid.NewGuid():D}");
                Assert.Equal(1, await insert.ExecuteNonQueryAsync());
            }

            Task<HttpResponseMessage>[] enrollments =
            [
                EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA)),
                EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB))
            ];
            await ClinicalRace.WaitUntilWaitingOnLocksAsync(host.ConnectionString, 2, enrollments);
            await hold.RollbackAsync();

            var responses = await Task.WhenAll(enrollments);
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
            var active = Assert.Single(await FacesAsync(host, patient.Id));
            Assert.Equal(BiometricReferenceStatus.Active, active.Status);
            Assert.Equal(1, Provider(host).Count);
            Assert.True(Provider(host).Holds(active.SubjectReference));
            Assert.Equal(1, await host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.EntityName == nameof(PatientBiometricReference) && a.Action == AuditActionType.Create)));

            // The database itself refuses a second ACTIVE face of the patient.
            var duplicate = PatientBiometricReference.Register(patient.Id, BiometricModality.Face, "SCIPH-FaceFake", $"face:{Guid.NewGuid():D}");
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => host.DbAsync(async db =>
            {
                db.PatientBiometricReferences.Add(duplicate);
                await db.SaveChangesAsync();
            }));
            Assert.Equal("UX_PatientBiometricReferences_ActiveFacePerPatient", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
        }

        // Biometrics.Manage enrolls and revokes; Patients.Identify verifies; both need active clinical staff who can see the
        // patient. No role bypass.
        [SkippableFact]
        public async Task OnlyAuthorizedStaffEnrollVerifyAndRevoke()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, clinic, reception, patient) = await SetupAsync();
            await using var _host = host;
            var enrolled = await JsonAsync(await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA)));
            var referenceId = enrolled.GetProperty("face").GetProperty("id").GetGuid();

            var anonymous = host.Anonymous();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"{Faces(patient.Id)}/verify", Form(FaceFixtures.Png(FaceFixtures.FaceA), Guid.NewGuid()))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Faces(patient.Id))).StatusCode);

            // Doctor: Patients.Identify, not Biometrics.Manage.
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            Assert.Equal("Match", await OutcomeAsync(await VerifyAsync(doctor, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA))));
            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync(Faces(patient.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await EnrollAsync(doctor, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await RevokeAsync(doctor, patient.Id, referenceId)).StatusCode);

            // Nurse: neither.
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse);
            Assert.Equal(HttpStatusCode.Forbidden, (await VerifyAsync(nurse, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await EnrollAsync(nurse, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA))).StatusCode);

            var adminWithoutStaff = await host.SignInAsync(SecurityRoles.Administrator, [], asClinicalStaff: false);
            await AssertProblemAsync(await VerifyAsync(adminWithoutStaff, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA)), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await EnrollAsync(adminWithoutStaff, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA)), HttpStatusCode.Forbidden);
            var inactive = await host.SignInAsync(SecurityRoles.Receptionist, [clinic], staffActive: false);
            await AssertProblemAsync(await VerifyAsync(inactive, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA)), HttpStatusCode.Forbidden);

            await AssertProblemAsync(await VerifyAsync(reception, Guid.NewGuid(), FaceFixtures.Png(FaceFixtures.FaceA)), HttpStatusCode.NotFound);

            // Another patient's reference, a fingerprint reference, or none: 404 on revoke; verify never takes a reference id.
            var other = await host.SeedPatientAsync("Luis", "Gómez");
            await AssertProblemAsync(await RevokeAsync(reception, other.Id, referenceId), HttpStatusCode.NotFound);
            await AssertProblemAsync(await VerifyAsync(reception, other.Id, FaceFixtures.Png(FaceFixtures.FaceA)), HttpStatusCode.Conflict);
            var fingerprint = PatientBiometricReference.Register(patient.Id, BiometricModality.Fingerprint, "R307", $"r307:device:{Guid.NewGuid():D}:slot:1");
            await host.DbAsync(async db =>
            {
                db.PatientBiometricReferences.Add(fingerprint);
                await db.SaveChangesAsync();
            });
            await AssertProblemAsync(await RevokeAsync(reception, patient.Id, fingerprint.Id), HttpStatusCode.NotFound);
            await AssertProblemAsync(await RevokeAsync(reception, patient.Id, Guid.NewGuid()), HttpStatusCode.NotFound);
            await AssertProblemAsync(await RevokeAsync(reception, patient.Id, referenceId, """{"reason":"Bogus"}"""), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await RevokeAsync(reception, patient.Id, referenceId, """{"subjectReference":"face:x"}"""), HttpStatusCode.BadRequest);
            Assert.Equal(BiometricReferenceStatus.Active, (await FacesAsync(host, patient.Id)).Single().Status);
        }

        // The input policy at the API: strict form, size, real decoding; never a client-chosen provider or threshold.
        [SkippableFact]
        public async Task InvalidCapturesAndFormsAreRejected()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient) = await SetupAsync();
            await using var _host = host;
            await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA));
            var verify = $"{Faces(patient.Id)}/verify";

            var forms = new (string Name, HttpContent Content)[]
            {
                ("zero bytes", Form([], Guid.NewGuid())),
                ("over 2 MB", Form([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[SkiaFaceCaptureNormalizer.MaxBytes]], Guid.NewGuid())),
                ("JPEG content type, text bytes", Form(Encoding.UTF8.GetBytes("not an image"), Guid.NewGuid(), "image/jpeg")),
                ("JPEG magic, corrupt", Form([0xFF, 0xD8, 0xFF, 0xE0, .. Enumerable.Repeat((byte)7, 5000)], Guid.NewGuid(), "image/jpeg")),
                ("GIF", Form([.. Encoding.ASCII.GetBytes("GIF89a"), .. new byte[64]], Guid.NewGuid(), "image/gif")),
                ("too small", Form(FaceFixtures.Png(FaceFixtures.FaceA, 320, 240), Guid.NewGuid())),
                ("no clientRequestId", Form(FaceFixtures.Png(FaceFixtures.FaceA), null)),
                ("empty clientRequestId", Form(FaceFixtures.Png(FaceFixtures.FaceA), Guid.Empty)),
                ("patientId field", Form(FaceFixtures.Png(FaceFixtures.FaceA), Guid.NewGuid(), extra: f => f.Add(new StringContent(patient.Id.ToString()), "patientId"))),
                ("provider field", Form(FaceFixtures.Png(FaceFixtures.FaceA), Guid.NewGuid(), extra: f => f.Add(new StringContent("Fake"), "provider"))),
                ("threshold field", Form(FaceFixtures.Png(FaceFixtures.FaceA), Guid.NewGuid(), extra: f => f.Add(new StringContent("0.1"), "threshold"))),
                ("referenceId field", Form(FaceFixtures.Png(FaceFixtures.FaceA), Guid.NewGuid(), extra: f => f.Add(new StringContent(Guid.NewGuid().ToString()), "referenceId"))),
                ("second file", Form(FaceFixtures.Png(FaceFixtures.FaceA), Guid.NewGuid(), extra: f => f.Add(new ByteArrayContent(FaceFixtures.Png(FaceFixtures.FaceA)), "capture2", "b.png")))
            };
            foreach (var (name, content) in forms)
            {
                var response = await reception.Client.PostAsync(verify, content);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {response.StatusCode}");
            }

            Assert.Equal(HttpStatusCode.UnsupportedMediaType,
                (await reception.Client.PostAsync(verify, new StringContent("""{"capture":"AAAA"}""", Encoding.UTF8, "application/json"))).StatusCode);
            Assert.Equal(0, await AuditsAsync(host, PatientFaceService.VerificationEntityName));
        }

        // A failing provider is a generic 503 for enrollment and verification; nothing is written.
        [SkippableFact]
        public async Task AFailingProviderIsAGeneric503()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient) = await SetupAsync();
            await using var _host = host;

            var enroll = await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.ProviderFailure));
            await AssertProblemAsync(enroll, HttpStatusCode.ServiceUnavailable);
            Assert.Empty(await FacesAsync(host, patient.Id));
            await EnrollAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceA));
            var verify = await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.ProviderFailure));
            await AssertProblemAsync(verify, HttpStatusCode.ServiceUnavailable);
            Assert.Equal(0, await AuditsAsync(host, PatientFaceService.VerificationEntityName));

            foreach (var body in new[] { await enroll.Content.ReadAsStringAsync(), await verify.Content.ReadAsStringAsync() })
            {
                foreach (var secret in new[] { "Fake", "SCIPH", "Exception", "at SCI_PH", "model", "threshold" })
                {
                    Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        // No image bytes, base64, subject reference, score or threshold in responses, audit entries or the Web BFF logs.
        [SkippableFact]
        public async Task NoBiometricDataLeaksToResponsesAuditOrLogs()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient) = await SetupAsync();
            await using var _host = host;
            var image = FaceFixtures.Png(FaceFixtures.FaceA);
            var base64 = Convert.ToBase64String(image)[..60];

            var enrolled = await EnrollAsync(reception, patient.Id, image);
            var referenceId = (await JsonAsync(enrolled)).GetProperty("face").GetProperty("id").GetGuid();
            var subject = (await FacesAsync(host, patient.Id)).Single().SubjectReference;
            var bodies = new List<string>
            {
                await enrolled.Content.ReadAsStringAsync(),
                await (await VerifyAsync(reception, patient.Id, image)).Content.ReadAsStringAsync(),
                await (await VerifyAsync(reception, patient.Id, FaceFixtures.Png(FaceFixtures.FaceB))).Content.ReadAsStringAsync(),
                await (await reception.Client.GetAsync(Faces(patient.Id))).Content.ReadAsStringAsync(),
                await (await RevokeAsync(reception, patient.Id, referenceId)).Content.ReadAsStringAsync()
            };
            var audits = (await host.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking().Where(a => a.PatientId == patient.Id).Select(a => a.ChangesJson).ToListAsync()))
                .Select(changes => changes ?? string.Empty).ToList();
            // The SQL log names the cleanup table; that name is not template data.
            var logs = host.Logs.Text.Replace("FaceTemplateDeletionJobs", "", StringComparison.Ordinal);

            foreach (var text in bodies.Concat(audits).Append(logs))
            {
                foreach (var secret in new[] { subject, "face:", base64, "score", "threshold", "similarity", "embedding", "template" })
                {
                    Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
                }
            }

            Assert.Contains(audits, a => a.Contains("Match", StringComparison.Ordinal));
            Assert.Contains(audits, a => a.Contains("fake-v1", StringComparison.Ordinal));
        }
    }
}
