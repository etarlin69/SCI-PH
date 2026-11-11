using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Documents;
using SCI_PH.Application.Features.Documents;
using SCI_PH.Domain.Entities.Documents;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Documents;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Clinical documents of an encounter through the real Web BFF over PostgreSQL, with the deterministic in-memory Storage
    // Service (no Azure): BFF -> Document Service -> Storage Service + metadata. Validation by real content, sanitized names,
    // server-generated paths, idempotent retries, audit of upload and download, and the encounter's access rules.
    [Collection(PostgresCollection.Name)]
    public sealed class EncounterDocumentsApiTests
    {
        private const string Encounters = "/api/web/encounters";
        private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");
        private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
        private readonly PostgresFixture _postgres;

        public EncounterDocumentsApiTests(PostgresFixture postgres) => _postgres = postgres;

        private sealed record Setup(ClinicalApiHost Host, InMemoryDocumentStorage Storage, StaffSession Doctor, StaffSession Nurse,
            StaffSession Reception, StaffSession OtherClinicDoctor, EncounterSummary Encounter);

        private async Task<Setup> InProgressAsync()
        {
            var storage = new InMemoryDocumentStorage();
            var host = await ClinicalApiHost.StartAsync(_postgres, services: s => s.UseDocumentStorage(storage));
            var clinic = await host.CreateClinicAsync();
            var other = await host.CreateClinicAsync("Otro hospital");
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse);
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var otherDoctor = await host.SignInAsync(SecurityRoles.Doctor, [other], staffType: ClinicalStaffType.Physician);
            var created = await reception.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", patientId = (await host.SeedPatientAsync()).Id });
            var encounter = await created.ReadAsAsync<EncounterSummary>();
            Assert.Equal(HttpStatusCode.Created, (await nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/vital-signs",
                new { type = "HeartRate", value = 90, measuredAtUtc = DateTimeOffset.UtcNow })).StatusCode);
            return new Setup(host, storage, doctor, nurse, reception, otherDoctor, encounter);
        }

        private static string Documents(Guid encounterId) => $"{Encounters}/{encounterId}/documents";

        private static MultipartFormDataContent Form(
            byte[] content, string fileName = "laboratorio.pdf", string contentType = "application/pdf", string? type = "LabResult",
            Guid? clientRequestId = null, Action<MultipartFormDataContent>? extra = null)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "file", fileName);
            if (type is not null)
            {
                form.Add(new StringContent(type), "type");
            }

            form.Add(new StringContent((clientRequestId ?? Guid.NewGuid()).ToString()), "clientRequestId");
            extra?.Invoke(form);
            return form;
        }

        private static Task<HttpResponseMessage> UploadAsync(StaffSession staff, Guid encounterId, MultipartFormDataContent form) =>
            staff.Client.PostAsync(Documents(encounterId), form);

        private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("encounters/", await response.Content.ReadAsStringAsync());
        }

        private static Task<List<ClinicalDocument>> RowsAsync(ClinicalApiHost host, Guid encounterId) =>
            host.DbAsync(db => db.ClinicalDocuments.AsNoTracking().Where(d => d.ClinicalEncounterId == encounterId).ToListAsync());

        [SkippableFact]
        public async Task AnUploadedDocumentIsListedAndDownloadedWithItsAudit()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;

            var uploaded = await UploadAsync(s.Doctor, id, Form(PdfBytes));

            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
            var json = await uploaded.Content.ReadAsStringAsync();
            Assert.DoesNotContain("blobPath", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("checksum", json, StringComparison.OrdinalIgnoreCase);
            var document = await uploaded.ReadAsAsync<ClinicalDocumentSummary>();
            Assert.Equal((id, ClinicalDocumentType.LabResult, "laboratorio.pdf", "application/pdf", (long)PdfBytes.Length),
                (document.EncounterId, document.Type, document.FileName, document.ContentType, document.SizeBytes));

            // Metadata in PostgreSQL, content in the storage at a server-generated path.
            var row = Assert.Single(await RowsAsync(s.Host, id));
            Assert.Equal(ClinicalDocumentStatus.Available, row.Status);
            Assert.Matches("^encounters/[0-9a-f]{32}/[0-9a-f]{32}$", row.BlobPath);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(PdfBytes)), row.ChecksumSha256!.Value);
            Assert.Equal(PdfBytes, s.Storage.Objects[row.BlobPath].Content);

            // The nurse of the same clinic lists and downloads it.
            var list = await (await s.Nurse.Client.GetAsync(Documents(id))).ReadAsAsync<List<ClinicalDocumentSummary>>();
            Assert.Equal(document.Id, Assert.Single(list).Id);
            var download = await s.Nurse.Client.GetAsync($"{Documents(id)}/{document.Id}/content");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(PdfBytes, await download.Content.ReadAsByteArrayAsync());
            Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));

            var audits = await s.Host.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .Where(a => a.EntityName == nameof(ClinicalDocument) && a.EntityId == document.Id.ToString()).ToListAsync());
            Assert.Equal([AuditActionType.Create, AuditActionType.Read], audits.OrderBy(a => a.OccurredAtUtc).Select(a => a.Action));
            Assert.All(audits, a => Assert.Equal(id, a.ClinicalEncounterId));
        }

        [SkippableFact]
        public async Task ARetryReplaysAndTheSameKeyWithAnotherFileIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var key = Guid.NewGuid();

            var first = await UploadAsync(s.Doctor, id, Form(PdfBytes, clientRequestId: key));
            var retry = await UploadAsync(s.Doctor, id, Form(PdfBytes, clientRequestId: key));
            var other = await UploadAsync(s.Doctor, id, Form(PngBytes, "foto.png", "image/png", clientRequestId: key));

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, retry.StatusCode));
            Assert.Equal((await first.ReadAsAsync<ClinicalDocumentSummary>()).Id, (await retry.ReadAsAsync<ClinicalDocumentSummary>()).Id);
            await AssertProblemAsync(other, HttpStatusCode.Conflict);
            Assert.Single(await RowsAsync(s.Host, id));
            Assert.Single(s.Storage.Objects);
        }

        [SkippableFact]
        public async Task OnlyTheRealContentDecidesAndTheNameIsOnlyADisplayName()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;

            // An executable renamed to .pdf, a PDF declared as an image, an empty file, one byte over the limit.
            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form([0x4D, 0x5A, 0x90, 0x00, 0x03], "informe.pdf")), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form(PdfBytes, "foto.png", "image/png")), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form([])), HttpStatusCode.BadRequest);
            var tooLarge = new byte[ClinicalDocumentService.MaxSizeBytes + 1];
            PdfBytes.CopyTo(tooLarge, 0);
            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form(tooLarge)), HttpStatusCode.BadRequest);
            // Unknown or numeric type, missing type, an extra field.
            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form(PdfBytes, type: "Secret")), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form(PdfBytes, type: "2")), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form(PdfBytes, type: null)), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form(PdfBytes, extra: f => f.Add(new StringContent("x"), "blobPath"))), HttpStatusCode.BadRequest);
            Assert.Empty(await RowsAsync(s.Host, id));
            Assert.Empty(s.Storage.Objects);

            // A path in the name never reaches the storage; the stored name gets the extension of the real type.
            var traversal = await UploadAsync(s.Doctor, id, Form(PngBytes, "../../etc/passwd<1>.exe", "image/png", "Imaging"));
            Assert.Equal(HttpStatusCode.Created, traversal.StatusCode);
            Assert.Equal("passwd1.png", (await traversal.ReadAsAsync<ClinicalDocumentSummary>()).FileName);
            Assert.Matches("^encounters/[0-9a-f]{32}/[0-9a-f]{32}$", Assert.Single(s.Storage.Objects.Keys));
        }

        [SkippableFact]
        public async Task OnlyStaffWhoReadTheEncounterUseItsDocuments()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var document = await (await UploadAsync(s.Doctor, id, Form(PdfBytes))).ReadAsAsync<ClinicalDocumentSummary>();
            var content = $"{Documents(id)}/{document.Id}/content";

            // Another clinic: nothing. Reception: no clinical read, so no upload into the clinical record either.
            await AssertProblemAsync(await s.OtherClinicDoctor.Client.GetAsync(Documents(id)), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await s.OtherClinicDoctor.Client.GetAsync(content), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await UploadAsync(s.OtherClinicDoctor, id, Form(PdfBytes)), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await s.Reception.Client.GetAsync(Documents(id)), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await UploadAsync(s.Reception, id, Form(PdfBytes)), HttpStatusCode.Forbidden);
            Assert.Equal(HttpStatusCode.Unauthorized, (await s.Host.Anonymous().GetAsync(content)).StatusCode);

            // A forged id: unknown encounter, unknown document, or a document requested through another encounter.
            await AssertProblemAsync(await s.Doctor.Client.GetAsync(Documents(Guid.NewGuid())), HttpStatusCode.NotFound);
            await AssertProblemAsync(await s.Doctor.Client.GetAsync($"{Documents(id)}/{Guid.NewGuid()}/content"), HttpStatusCode.NotFound);
            var otherEncounter = await (await s.Reception.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", patientId = (await s.Host.SeedPatientAsync()).Id }))
                .ReadAsAsync<EncounterSummary>();
            await AssertProblemAsync(await s.Doctor.Client.GetAsync($"{Documents(otherEncounter.Id)}/{document.Id}/content"), HttpStatusCode.NotFound);
            Assert.Single(await RowsAsync(s.Host, id));
        }

        [SkippableFact]
        public async Task StorageFailuresAndMissingContentAreControlled()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var document = await (await UploadAsync(s.Doctor, id, Form(PdfBytes))).ReadAsAsync<ClinicalDocumentSummary>();

            // Storage unreachable: 503, no metadata without its content.
            s.Storage.Unavailable = true;
            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form(PngBytes, "foto.png", "image/png")), HttpStatusCode.ServiceUnavailable);
            await AssertProblemAsync(await s.Doctor.Client.GetAsync($"{Documents(id)}/{document.Id}/content"), HttpStatusCode.ServiceUnavailable);
            Assert.Single(await RowsAsync(s.Host, id));

            // Metadata whose object is gone: a 404, not a server error.
            s.Storage.Unavailable = false;
            s.Storage.Objects.Clear();
            await AssertProblemAsync(await s.Doctor.Client.GetAsync($"{Documents(id)}/{document.Id}/content"), HttpStatusCode.NotFound);
        }

        [SkippableFact]
        public async Task AClosedEncounterKeepsItsDocumentsButAcceptsNoNewOnes()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var document = await (await UploadAsync(s.Doctor, id, Form(PdfBytes))).ReadAsAsync<ClinicalDocumentSummary>();
            Assert.Equal(HttpStatusCode.NoContent, (await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { dischargeNote = (string?)null })).StatusCode);

            await AssertProblemAsync(await UploadAsync(s.Doctor, id, Form(PngBytes, "foto.png", "image/png")), HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.OK, (await s.Doctor.Client.GetAsync($"{Documents(id)}/{document.Id}/content")).StatusCode);
            Assert.Single(s.Storage.Objects);
        }

        // Without a configured storage (the default) the feature answers a generic 503 and writes nothing.
        [SkippableFact]
        public async Task WithoutStorageConfiguredUploadsAreUnavailable()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var encounter = await (await reception.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", patientId = (await host.SeedPatientAsync()).Id }))
                .ReadAsAsync<EncounterSummary>();

            await AssertProblemAsync(await UploadAsync(doctor, encounter.Id, Form(PdfBytes)), HttpStatusCode.ServiceUnavailable);
            Assert.Empty(await RowsAsync(host, encounter.Id));
            Assert.Empty(await (await doctor.Client.GetAsync(Documents(encounter.Id))).ReadAsAsync<List<JsonElement>>());
        }
    }
}
