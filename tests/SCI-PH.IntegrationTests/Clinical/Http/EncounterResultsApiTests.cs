using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Clinical results of an encounter through the real Web BFF over PostgreSQL: the Domain lifecycle (Pending -> Final ->
    // Corrected, or Cancelled), Results.Write for changes and Encounters.Read for reading, the encounter's clinic and status
    // rules, audit of every change, and two people completing the same order (one wins, the other is a 409).
    [Collection(PostgresCollection.Name)]
    public sealed class EncounterResultsApiTests
    {
        private const string Encounters = "/api/web/encounters";
        private readonly PostgresFixture _postgres;

        public EncounterResultsApiTests(PostgresFixture postgres) => _postgres = postgres;

        private sealed record Setup(ClinicalApiHost Host, StaffSession Doctor, StaffSession Nurse, StaffSession OtherClinicDoctor, EncounterSummary Encounter);

        // A new encounter (Created): nothing recorded yet.
        private async Task<Setup> CreatedAsync()
        {
            var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var other = await host.CreateClinicAsync("Otro hospital");
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse);
            var otherDoctor = await host.SignInAsync(SecurityRoles.Doctor, [other], staffType: ClinicalStaffType.Physician);
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var encounter = await (await reception.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", patientId = (await host.SeedPatientAsync()).Id }))
                .ReadAsAsync<EncounterSummary>();
            return new Setup(host, doctor, nurse, otherDoctor, encounter);
        }

        private static string Results(Guid encounterId) => $"{Encounters}/{encounterId}/results";

        private static Task<HttpResponseMessage> RequestAsync(StaffSession staff, Guid encounterId, string name = "Hemoglobina", string type = "Laboratory") =>
            staff.Client.PostAsJsonAsync(Results(encounterId), new { type, name, referenceRange = "12-16 g/dL" });

        private static async Task<ClinicalResultSummary> RequestedAsync(StaffSession staff, Guid encounterId, string name = "Hemoglobina")
        {
            var response = await RequestAsync(staff, encounterId, name);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<ClinicalResultSummary>();
        }

        private static Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            return Task.CompletedTask;
        }

        private static Task<ClinicalResult> RowAsync(ClinicalApiHost host, Guid resultId) =>
            host.DbAsync(db => db.ClinicalResults.AsNoTracking().SingleAsync(r => r.Id == resultId));

        [SkippableFact]
        public async Task AnOrderIsRecordedCorrectedAndReadWithItsAudit()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await CreatedAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;

            var order = await RequestedAsync(s.Doctor, id);
            Assert.Equal((ClinicalResultStatus.Pending, ClinicalResultType.Laboratory, "12-16 g/dL", (string?)null),
                (order.Status, order.Type, order.ReferenceRange, order.ResultText));
            // The first record starts care.
            Assert.Equal(ClinicalEncounterStatus.InProgress, await s.Host.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == id).Select(e => e.Status).SingleAsync()));

            var final = await s.Doctor.Client.PostAsJsonAsync($"{Results(id)}/{order.Id}/final", new { numericValue = 13.5m, unit = "g/dL" });
            Assert.Equal(HttpStatusCode.OK, final.StatusCode);
            var recorded = await final.ReadAsAsync<ClinicalResultSummary>();
            Assert.Equal((ClinicalResultStatus.Final, 13.5m, "g/dL"), (recorded.Status, recorded.NumericValue, recorded.Unit));
            Assert.False(string.IsNullOrWhiteSpace(recorded.RecordedByDisplayName));
            Assert.NotNull(recorded.ResultedAtUtc);

            var corrected = await (await s.Doctor.Client.PostAsJsonAsync($"{Results(id)}/{order.Id}/correct", new { numericValue = 12.9m, unit = "g/dL" }))
                .ReadAsAsync<ClinicalResultSummary>();
            Assert.Equal((ClinicalResultStatus.Corrected, 12.9m), (corrected.Status, corrected.NumericValue));

            // The nurse reads them (clinical read), but cannot change them (no Results.Write).
            var list = await (await s.Nurse.Client.GetAsync(Results(id))).ReadAsAsync<List<ClinicalResultSummary>>();
            Assert.Equal(order.Id, Assert.Single(list).Id);
            await AssertProblemAsync(await RequestAsync(s.Nurse, id), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await s.Nurse.Client.PostAsJsonAsync($"{Results(id)}/{order.Id}/correct", new { numericValue = 1m }), HttpStatusCode.Forbidden);

            var audits = await s.Host.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .Where(a => a.EntityName == nameof(ClinicalResult) && a.EntityId == order.Id.ToString()).Select(a => a.Action).ToListAsync());
            Assert.Equal(1, audits.Count(a => a == AuditActionType.Create));
            Assert.Equal(2, audits.Count(a => a == AuditActionType.Update));
        }

        [SkippableFact]
        public async Task OnlyTheDomainLifecycleIsAllowed()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await CreatedAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var pending = await RequestedAsync(s.Doctor, id);
            var url = $"{Results(id)}/{pending.Id}";

            // A pending order cannot be corrected; a value is required; a final one cannot be finalized again or cancelled.
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync($"{url}/correct", new { resultText = "x" }), HttpStatusCode.Conflict);
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync($"{url}/final", new { unit = "g/dL" }), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync($"{url}/final", new { resultText = "x", resultedAtUtc = DateTimeOffset.UtcNow.AddHours(1) }), HttpStatusCode.BadRequest);
            Assert.Equal(HttpStatusCode.OK, (await s.Doctor.Client.PostAsJsonAsync($"{url}/final", new { resultText = "Normal" })).StatusCode);
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync($"{url}/final", new { resultText = "Otra" }), HttpStatusCode.Conflict);
            await AssertProblemAsync(await s.Doctor.Client.PostAsync($"{url}/cancel", null), HttpStatusCode.Conflict);
            Assert.Equal(("Normal", ClinicalResultStatus.Final), ((await RowAsync(s.Host, pending.Id)).ResultText, (await RowAsync(s.Host, pending.Id)).Status));

            // A pending order is cancelled once, and then nothing else applies to it.
            var toCancel = await RequestedAsync(s.Doctor, id, "Glucemia");
            Assert.Equal(ClinicalResultStatus.Cancelled, (await (await s.Doctor.Client.PostAsync($"{Results(id)}/{toCancel.Id}/cancel", null)).ReadAsAsync<ClinicalResultSummary>()).Status);
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync($"{Results(id)}/{toCancel.Id}/final", new { resultText = "x" }), HttpStatusCode.Conflict);

            // Malformed requests: unknown or numeric type, missing name, extra member.
            await AssertProblemAsync(await RequestAsync(s.Doctor, id, type: "Telepathy"), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync(Results(id), new { type = 1, name = "x" }), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync(Results(id), new { type = "Laboratory" }), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync(Results(id), new { type = "Laboratory", name = "x", status = "Final" }), HttpStatusCode.BadRequest);
        }

        [SkippableFact]
        public async Task TwoPeopleCompletingTheSameOrderNeverOverwriteEachOther()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await CreatedAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var order = await RequestedAsync(s.Doctor, id);
            var url = $"{Results(id)}/{order.Id}/final";

            var responses = await Task.WhenAll(
                s.Doctor.Client.PostAsJsonAsync(url, new { resultText = "Primero" }),
                s.Doctor.Client.PostAsJsonAsync(url, new { resultText = "Segundo" }));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
            var winner = await responses.Single(r => r.StatusCode == HttpStatusCode.OK).ReadAsAsync<ClinicalResultSummary>();
            Assert.Equal(winner.ResultText, (await RowAsync(s.Host, order.Id)).ResultText);
            Assert.Equal(1, await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.EntityName == nameof(ClinicalResult) && a.EntityId == order.Id.ToString() && a.Action == AuditActionType.Update)));
        }

        [SkippableFact]
        public async Task TheEncountersClinicAndStatusRulesApply()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await CreatedAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var order = await RequestedAsync(s.Doctor, id);

            // Another clinic: no read, no write. Forged ids: 404.
            await AssertProblemAsync(await s.OtherClinicDoctor.Client.GetAsync(Results(id)), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await RequestAsync(s.OtherClinicDoctor, id), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await s.OtherClinicDoctor.Client.PostAsJsonAsync($"{Results(id)}/{order.Id}/final", new { resultText = "x" }), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await s.Doctor.Client.GetAsync(Results(Guid.NewGuid())), HttpStatusCode.NotFound);
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync($"{Results(id)}/{Guid.NewGuid()}/final", new { resultText = "x" }), HttpStatusCode.NotFound);
            Assert.Equal(HttpStatusCode.Unauthorized, (await s.Host.Anonymous().GetAsync(Results(id))).StatusCode);

            // A result is only reachable through its own encounter.
            var reception = await s.Host.SignInAsync(SecurityRoles.Receptionist, [await s.Host.DbAsync(db => db.Clinics.SingleAsync(c => c.Id == s.Encounter.ClinicId))]);
            var other = await (await reception.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", patientId = (await s.Host.SeedPatientAsync()).Id }))
                .ReadAsAsync<EncounterSummary>();
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync($"{Results(other.Id)}/{order.Id}/final", new { resultText = "x" }), HttpStatusCode.NotFound);

            // Once closed: no new orders or values, and the results stay readable.
            Assert.Equal(HttpStatusCode.NoContent, (await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { dischargeNote = (string?)null })).StatusCode);
            await AssertProblemAsync(await RequestAsync(s.Doctor, id), HttpStatusCode.Conflict);
            await AssertProblemAsync(await s.Doctor.Client.PostAsJsonAsync($"{Results(id)}/{order.Id}/final", new { resultText = "x" }), HttpStatusCode.Conflict);
            Assert.Single(await (await s.Doctor.Client.GetAsync(Results(id))).ReadAsAsync<List<JsonElement>>());
        }
    }
}
