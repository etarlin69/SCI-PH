using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Infrastructure.Biometrics.Face.OpenCv;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;

namespace SCI_PH.IntegrationTests.Biometrics.Face
{
    // End to end with the REAL local provider: the Web BFF over PostgreSQL, the encrypted template store on Azurite and the
    // pinned models, on synthetic faces. OPT-IN (SyntheticFaces) and needs Docker. The automated part of the manual E2E of
    // ADR-012 PR 2 (the webcam part stays manual).
    [Collection(PostgresCollection.Name)]
    public sealed class OpenCvFaceApiTests : IClassFixture<AzuriteFixture>
    {
        private readonly PostgresFixture _postgres;
        private readonly AzuriteFixture _azurite;

        public OpenCvFaceApiTests(PostgresFixture postgres, AzuriteFixture azurite)
        {
            _postgres = postgres;
            _azurite = azurite;
        }

        private static string Faces(Guid patientId) => $"/api/web/patients/{patientId}/faces";

        private static MultipartFormDataContent Form(byte[] capture)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(capture);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "capture", "capture.png");
            form.Add(new StringContent(Guid.NewGuid().ToString()), "clientRequestId");
            return form;
        }

        private static Task<HttpResponseMessage> EnrollAsync(StaffSession staff, Guid patientId, byte[] capture) =>
            staff.Client.PostAsync($"{Faces(patientId)}/enroll", Form(capture));

        private static Task<HttpResponseMessage> VerifyAsync(StaffSession staff, Guid patientId, byte[] capture) =>
            staff.Client.PostAsync($"{Faces(patientId)}/verify", Form(capture));

        private static async Task<string> OutcomeAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("outcome").GetString()!;
        }

        [SkippableFact]
        public async Task TheRealProviderEnrollsVerifiesRevokesAndCleansUpEncryptedTemplates()
        {
            Skip.IfNot(SyntheticFaces.Enabled, SyntheticFaces.SkipReason);
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            Skip.IfNot(_azurite.Available, _azurite.SkipReason);
            var containerName = $"face-templates-{Guid.NewGuid():N}"[..40];
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await using var host = await ClinicalApiHost.StartAsync(_postgres, new Dictionary<string, string?>
            {
                ["FaceVerification:Provider"] = "OpenCv",
                ["FaceVerification:OpenCv:YuNetModelPath"] = SyntheticFaces.YuNet,
                ["FaceVerification:OpenCv:SFaceModelPath"] = SyntheticFaces.SFace,
                ["FaceVerification:OpenCv:Templates:ConnectionString"] = _azurite.ConnectionString,
                ["FaceVerification:OpenCv:Templates:Container"] = containerName,
                ["FaceVerification:OpenCv:Encryption:ActiveKeyId"] = "k1",
                ["FaceVerification:OpenCv:Encryption:Keys:k1"] = key
            });
            var clinic = await host.CreateClinicAsync();
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var patient = await host.SeedPatientAsync();
            var container = new BlobContainerClient(_azurite.ConnectionString, containerName);
            using var a = SyntheticFaces.Face(0);
            using var aAgain = SyntheticFaces.Transformed(a);
            using var b = SyntheticFaces.Face(1);
            using var pair = SyntheticFaces.Pair(a, b);
            using var black = SyntheticFaces.Black();

            // Enrollment: only "face:<guid>" in the clinical schema; one encrypted, opaque blob in the private store.
            var enrolled = await EnrollAsync(reception, patient.Id, SyntheticFaces.Png(a));
            Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
            var referenceId = JsonDocument.Parse(await enrolled.Content.ReadAsStringAsync()).RootElement.GetProperty("face").GetProperty("id").GetGuid();
            var reference = await host.DbAsync(db => db.PatientBiometricReferences.AsNoTracking().SingleAsync(r => r.Id == referenceId));
            Assert.Equal(("SCIPH-FaceOpenCV", FaceModels.AlgorithmVersion), (reference.Provider, reference.AlgorithmVersion));
            Assert.True(FaceSubjectReference.TryParse(reference.SubjectReference, out var templateId));
            var blob = container.GetBlobClient(templateId.ToString("D"));
            var envelope = (await blob.DownloadContentAsync()).Value.Content.ToArray();
            Assert.Equal("SFE"u8.ToArray(), envelope[..3]);
            Assert.DoesNotContain(patient.Id.ToString(), Encoding.Latin1.GetString(envelope));

            // 1:1 against the patient's own reference.
            Assert.Equal("Match", await OutcomeAsync(await VerifyAsync(reception, patient.Id, SyntheticFaces.Png(aAgain))));
            Assert.Equal("NoMatch", await OutcomeAsync(await VerifyAsync(reception, patient.Id, SyntheticFaces.Png(b))));
            Assert.Equal("NoFace", await OutcomeAsync(await VerifyAsync(reception, patient.Id, SyntheticFaces.Png(black))));
            Assert.Equal("MultipleFaces", await OutcomeAsync(await VerifyAsync(reception, patient.Id, SyntheticFaces.Png(pair))));

            // An active reference whose template is gone is a 503, never NoMatch; back in place, it matches again.
            await blob.DeleteAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await VerifyAsync(reception, patient.Id, SyntheticFaces.Png(aAgain))).StatusCode);
            await blob.UploadAsync(new BinaryData(envelope));
            Assert.Equal("Match", await OutcomeAsync(await VerifyAsync(reception, patient.Id, SyntheticFaces.Png(aAgain))));

            // Revocation: logical first, then the durable cleanup deletes the blob and completes once.
            var revoked = await reception.Client.PostAsync($"{Faces(patient.Id)}/{referenceId}/revoke",
                new StringContent("""{"reason":"PatientRequest"}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
            Assert.False((await blob.ExistsAsync()).Value);
            var job = await host.DbAsync(db => db.FaceTemplateDeletionJobs.AsNoTracking().SingleAsync());
            Assert.Equal((FaceTemplateDeletionStatus.Completed, FaceTemplateDeletionOutcome.Deleted), (job.Status, job.Outcome));
            Assert.Equal(1, await host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(e =>
                e.EntityName == nameof(PatientBiometricReference) && e.ActorType == AuditActorType.System)));
            Assert.Equal(HttpStatusCode.Conflict, (await VerifyAsync(reception, patient.Id, SyntheticFaces.Png(aAgain))).StatusCode);

            // A new face can be enrolled and verified.
            Assert.Equal(HttpStatusCode.Created, (await EnrollAsync(reception, patient.Id, SyntheticFaces.Png(b))).StatusCode);
            Assert.Equal("Match", await OutcomeAsync(await VerifyAsync(reception, patient.Id, SyntheticFaces.Png(b))));
            // Six recorded verifications: the 503 and the 409 record nothing.
            Assert.Equal(6, await host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(e => e.EntityName == PatientFaceService.VerificationEntityName)));

            // Nothing biometric or secret in the logs.
            var logs = host.Logs.Text.Replace("FaceTemplateDeletionJobs", "", StringComparison.Ordinal);
            foreach (var secret in new[] { reference.SubjectReference, key, "embedding", "similarity", "0.363" })
            {
                Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Contains("FaceTemplateMissing", logs);
        }
    }
}
