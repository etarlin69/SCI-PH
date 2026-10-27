using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Clinical.Idempotency;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // A reading of vital signs on a real prehospital emergency through the Mobile BFF over PostgreSQL: 1 to 7 values in one
    // transaction with one audit entry each, the start of the encounter and the receipt; replay, conflict and the race of
    // the same reading without duplicates or 500s; access by assignment; the Web keeps its own rules.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileVitalSignsApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string WebEncounters = "/api/web/encounters";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly PostgresFixture _postgres;

        public MobileVitalSignsApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static DateTimeOffset NowToTheMillisecond()
        {
            var now = DateTimeOffset.UtcNow;
            return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        private static string Url(Guid emergencyId) => $"{Emergencies}/{emergencyId}/vital-signs";

        private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv);

        private static string AllSevenJson(Guid key, string recordedAt, string temperature = "37.5") =>
            $$"""{"clientRequestId":"{{key}}","recordedAt":"{{recordedAt}}","heartRate":88,"respiratoryRate":18,"systolicPressure":120,"diastolicPressure":80,"oxygenSaturation":97,"temperature":{{temperature}},"glasgow":15}""";

        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, Guid emergencyId, string json) =>
            client.PostAsync(Url(emergencyId), new StringContent(json, Encoding.UTF8, "application/json"));

        private static async Task<PrehospitalEmergencySummary> OpenAsync(StaffSession paramedic, DateTimeOffset startedAt)
        {
            var response = await paramedic.Client.PostAsJsonAsync(Emergencies, new { clientRequestId = Guid.NewGuid(), startedAt, chiefComplaint = "Dolor torácico" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<PrehospitalEmergencySummary>();
        }

        private static Task<int> VitalsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.VitalSigns.CountAsync(v => v.ClinicalEncounterId == emergencyId));

        private static Task<int> VitalAuditsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergencyId && a.EntityName == nameof(VitalSign)));

        private static Task<int> ReceiptsAsync(MobileEmergencyHost host) =>
            host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == PrehospitalEmergencyService.RecordVitalSignsOperation));

        [SkippableFact]
        public async Task AReadingOfSevenIsRecordedInOneTransactionOnTheNnEmergency()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var encountersBefore = await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync());
            var recordedAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            var response = await PostJsonAsync(paramedic.Client, opened.Id, AllSevenJson(key, Utc(recordedAt), "37.50"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null(response.Headers.Location);
            var reading = await response.ReadAsAsync<EmergencyVitalSignsSummary>();
            Assert.Equal((opened.Id, recordedAt), (reading.EncounterId, reading.RecordedAtUtc));
            Assert.Equal(
            [
                (VitalSignType.HeartRate, 88m, "/min"), (VitalSignType.RespiratoryRate, 18m, "/min"),
                (VitalSignType.SystolicBloodPressure, 120m, "mm[Hg]"), (VitalSignType.DiastolicBloodPressure, 80m, "mm[Hg]"),
                (VitalSignType.Temperature, 37.5m, "Cel"), (VitalSignType.OxygenSaturation, 97m, "%"),
                (VitalSignType.GlasgowComaScale, 15m, "{score}")
            ], reading.Measurements.Select(m => (m.Type, m.Value, m.Unit)));

            var rows = await host.Web.DbAsync(db => db.VitalSigns.AsNoTracking().Where(v => v.ClinicalEncounterId == opened.Id).ToListAsync());
            Assert.Equal(7, rows.Count);
            Assert.All(rows, v => Assert.Equal((recordedAt, paramedic.StaffId), (v.MeasuredAtUtc, v.MeasuredByStaffId)));
            Assert.Equal(reading.Measurements.Select(m => (m.Type, m.Value, m.Unit)).Order(), rows.Select(v => (v.Type, v.Value, v.Unit)).Order());

            var encounter = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == opened.Id));
            Assert.Equal((ClinicalEncounterStatus.InProgress, (Guid?)null, (Guid?)null), (encounter.Status, encounter.PatientId, encounter.ClinicId));
            Assert.Equal(opened.TemporaryPatientCode, encounter.TemporaryPatientCode!.Value);
            Assert.Equal(encountersBefore, await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync()));
            Assert.Equal(7, await VitalAuditsAsync(host, opened.Id));
            Assert.Equal(7, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.ClinicalEncounterId == opened.Id && a.EntityName == nameof(VitalSign) && a.Action == AuditActionType.Create && a.ActorStaffId == paramedic.StaffId)));
            var receipt = await host.Web.DbAsync(db => db.IdempotencyReceipts.AsNoTracking().SingleAsync(r => r.ClientRequestId == key));
            Assert.Equal((PrehospitalEmergencyService.RecordVitalSignsOperation, opened.Id), (receipt.Operation, receipt.ResourceId));
        }

        [SkippableFact]
        public async Task AnIdentifiedEmergencyAcceptsAPartialReading()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1985, 3, 14), BiologicalSex.Female);
            patient.AddIdentifier(PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Manual, isPrimary: true);
            await host.Web.DbAsync(async db => { db.Patients.Add(patient); await db.SaveChangesAsync(); });
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{opened.Id}/identify/document",
                new { clientRequestId = Guid.NewGuid(), documentType = "NationalId", documentNumber = "001-1234567-8" })).StatusCode);

            var response = await paramedic.Client.PostAsJsonAsync(Url(opened.Id),
                new { clientRequestId = Guid.NewGuid(), recordedAt = NowToTheMillisecond(), heartRate = 110, oxygenSaturation = 91 });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(2, await VitalsAsync(host, opened.Id));
            Assert.Equal(2, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.ClinicalEncounterId == opened.Id && a.EntityName == nameof(VitalSign) && a.PatientId == patient.Id)));
        }

        // The wire contract: explicit offset, integers except temperature (2 decimals), at least one value, no unit or
        // unknown field. Every rejection is a 400 ProblemDetails that writes nothing.
        [SkippableFact]
        public async Task InvalidReadingsAreRejectedWithoutWritingAnything()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var now = NowToTheMillisecond().AddMinutes(-1);
            string Json(string body) => $$"""{"clientRequestId":"{{Guid.NewGuid()}}",{{body}}}""";
            var at = $"\"recordedAt\":\"{Utc(now)}\"";

            foreach (var json in new[]
            {
                Json($"\"recordedAt\":\"{now.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss}\",\"heartRate\":80"),   // no offset
                Json("\"recordedAt\":\"not a date\",\"heartRate\":80"),
                Json(at),                                                                                // no value
                Json($"{at},\"heartRate\":72.5"),                                                       // integer field
                Json($"{at},\"temperature\":37.555"),                                                   // > 2 decimals
                Json($"{at},\"temperature\":45.01"),
                Json($"{at},\"heartRate\":301"),
                Json($"{at},\"heartRate\":80,\"unit\":\"/min\""),                                       // unknown field
                Json($"{at},\"heartRate\":80,\"weight\":70"),
                Json($"\"recordedAt\":\"{Utc(NowToTheMillisecond().AddMinutes(-11))}\",\"heartRate\":80"),  // before start
                Json($"\"recordedAt\":\"{Utc(NowToTheMillisecond().AddMinutes(10))}\",\"heartRate\":80"),   // beyond +5 min
                AllSevenJson(Guid.NewGuid(), Utc(now)).Replace("\"glasgow\":15", "\"glasgow\":2"),       // one of seven invalid
                $$"""{"clientRequestId":"{{Guid.Empty}}",{{at}},"heartRate":80}"""
            })
            {
                var response = await PostJsonAsync(paramedic.Client, opened.Id, json);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {json}");
                await AssertProblemAsync(response, HttpStatusCode.BadRequest);
            }

            Assert.Equal(0, await VitalsAsync(host, opened.Id));
            Assert.Equal(0, await VitalAuditsAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
            Assert.Equal(ClinicalEncounterStatus.Created, await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == opened.Id).Select(e => e.Status).SingleAsync()));
        }

        // "Z" or an explicit offset: the same instant either way, stored in UTC.
        [SkippableFact]
        public async Task RecordedAtTakesZOrAnExplicitOffset()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var instant = NowToTheMillisecond().AddMinutes(-2);

            var z = await PostJsonAsync(paramedic.Client, opened.Id, $$"""{"clientRequestId":"{{Guid.NewGuid()}}","recordedAt":"{{Utc(instant)}}","heartRate":80}""");
            var offset = await PostJsonAsync(paramedic.Client, opened.Id,
                $$"""{"clientRequestId":"{{Guid.NewGuid()}}","recordedAt":"{{instant.ToOffset(TimeSpan.FromHours(-4)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", Inv)}}","heartRate":81}""");

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (z.StatusCode, offset.StatusCode));
            Assert.All(await host.Web.DbAsync(db => db.VitalSigns.AsNoTracking().Where(v => v.ClinicalEncounterId == opened.Id).ToListAsync()),
                v => Assert.Equal(instant, v.MeasuredAtUtc));
        }

        // Same key + same reading (37.5 or 37.50, Z or offset): 200 with the same reading, nothing new. Same key with
        // another reading or emergency: 409. Another key at the same instant is another reading (time series).
        [SkippableFact]
        public async Task RetryingReplaysAndAReusedKeyWithOtherDataIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var other = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var recordedAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            var first = await PostJsonAsync(paramedic.Client, opened.Id, AllSevenJson(key, Utc(recordedAt), "37.5"));
            var replay = await PostJsonAsync(paramedic.Client, opened.Id,
                AllSevenJson(key, recordedAt.ToOffset(TimeSpan.FromHours(-4)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", Inv), "37.50"));

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, replay.StatusCode));
            Assert.Equal(
                JsonSerializer.Serialize(await first.ReadAsAsync<EmergencyVitalSignsSummary>()),
                JsonSerializer.Serialize(await replay.ReadAsAsync<EmergencyVitalSignsSummary>()));

            foreach (var (emergencyId, json) in new[]
            {
                (opened.Id, AllSevenJson(key, Utc(recordedAt), "37.6")),
                (opened.Id, AllSevenJson(key, Utc(recordedAt.AddSeconds(-1)))),
                (opened.Id, AllSevenJson(key, Utc(recordedAt)).Replace(",\"glasgow\":15", "")),
                (other.Id, AllSevenJson(key, Utc(recordedAt)))
            })
            {
                await AssertProblemAsync(await PostJsonAsync(paramedic.Client, emergencyId, json), HttpStatusCode.Conflict);
            }

            Assert.Equal(7, await VitalsAsync(host, opened.Id));
            Assert.Equal(7, await VitalAuditsAsync(host, opened.Id));
            Assert.Equal(0, await VitalsAsync(host, other.Id));
            Assert.Equal(1, await ReceiptsAsync(host));

            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, AllSevenJson(Guid.NewGuid(), Utc(recordedAt)))).StatusCode);
            Assert.Equal(14, await VitalsAsync(host, opened.Id));
        }

        [SkippableFact]
        public async Task AConcurrentDoubleSubmitRecordsTheReadingOnce()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);

            for (var round = 0; round < 5; round++)
            {
                var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
                var json = AllSevenJson(Guid.NewGuid(), Utc(NowToTheMillisecond().AddMinutes(-1)));
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Submit() => Task.Run(async () => { start.Wait(); return await PostJsonAsync(paramedic.Client, opened.Id, json); });
                var tasks = new[] { Submit(), Submit() };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], responses.Select(r => r.StatusCode).Order());
                Assert.Equal(7, await VitalsAsync(host, opened.Id));
                Assert.Equal(7, await VitalAuditsAsync(host, opened.Id));
            }

            Assert.Equal(5, await ReceiptsAsync(host));
        }

        // Deterministic race of the same reading: another transaction holds its receipt and the start of the emergency,
        // uncommitted. The request waits on them and, when they commit, answers 200 from the receipt: no duplicate, no 500.
        [SkippableFact]
        public async Task LosingTheRaceToTheSameReadingReturnsTheWinner()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var recordedAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            await using var other = PostgresFixture.Clinical(host.ConnectionString);
            await using var transaction = await other.Database.BeginTransactionAsync();
            var encounter = await other.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, DateTimeOffset.UtcNow);
            other.VitalSigns.Add(VitalSign.Record(opened.Id, VitalSignType.HeartRate, 80, recordedAt, paramedic.StaffId));
            other.IdempotencyReceipts.Add(IdempotencyReceiptEntry.From(new IdempotencyReceipt(
                new IdempotencyKey(paramedic.UserId, PrehospitalEmergencyService.RecordVitalSignsOperation, key),
                IdempotencyRequestHash.Compute(opened.Id.ToString("D"), IdempotencyRequestHash.Instant(recordedAt), "HeartRate", "80"),
                opened.Id, DateTimeOffset.UtcNow)));
            await other.SaveChangesAsync();

            var request = PostJsonAsync(paramedic.Client, opened.Id, $$"""{"clientRequestId":"{{key}}","recordedAt":"{{Utc(recordedAt)}}","heartRate":80}""");
            await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
            await transaction.CommitAsync();
            var response = await request;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, await VitalsAsync(host, opened.Id));
            Assert.Equal(0, await VitalAuditsAsync(host, opened.Id));
            Assert.Equal(1, await ReceiptsAsync(host));
        }

        [SkippableFact]
        public async Task OnlyTheAssignedParamedicRecordsVitalSigns()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var otherParamedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            string Body() => $$"""{"clientRequestId":"{{Guid.NewGuid()}}","recordedAt":"{{Utc(NowToTheMillisecond())}}","heartRate":80}""";

            Assert.Equal(HttpStatusCode.Unauthorized, (await PostJsonAsync(host.Anonymous(), opened.Id, Body())).StatusCode);
            foreach (var (role, type) in new[]
            {
                (SecurityRoles.Doctor, ClinicalStaffType.Physician),
                (SecurityRoles.Nurse, ClinicalStaffType.Nurse),
                (SecurityRoles.Receptionist, ClinicalStaffType.OtherClinical)
            })
            {
                var user = await host.SignInAsync(role, [clinic], staffType: type);
                await AssertProblemAsync(await PostJsonAsync(user.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            }

            await AssertProblemAsync(await PostJsonAsync(otherParamedic.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, Guid.NewGuid(), Body()), HttpStatusCode.NotFound);
            Assert.Equal(0, await VitalsAsync(host, opened.Id));

            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, Body())).StatusCode);
        }

        // The Web keeps its rules: a clinic encounter is written by its clinic's staff only and a future measuredAt is a
        // 400 (the +5 minutes are for the Mobile prehospital reading only).
        [SkippableFact]
        public async Task TheWebVitalSignsKeepTheClinicRuleAndRejectTheFuture()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinicA = await host.CreateClinicAsync("Hospital A");
            var clinicB = await host.CreateClinicAsync("Hospital B");
            var nurseA = await host.SignInAsync(SecurityRoles.Nurse, [clinicA], staffType: ClinicalStaffType.Nurse);
            var nurseB = await host.SignInAsync(SecurityRoles.Nurse, [clinicB], staffType: ClinicalStaffType.Nurse);
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, DateTimeOffset.UtcNow.AddMinutes(-30), clinicA.Id);
            await host.DbAsync(async db => { db.ClinicalEncounters.Add(encounter); await db.SaveChangesAsync(); });
            object Body(DateTimeOffset measuredAtUtc) => new { type = "HeartRate", value = 80, measuredAtUtc };

            Assert.Equal(HttpStatusCode.Created, (await nurseA.Client.PostAsJsonAsync($"{WebEncounters}/{encounter.Id}/vital-signs", Body(DateTimeOffset.UtcNow.AddMinutes(-1)))).StatusCode);
            await AssertProblemAsync(await nurseB.Client.PostAsJsonAsync($"{WebEncounters}/{encounter.Id}/vital-signs", Body(DateTimeOffset.UtcNow.AddMinutes(-1))), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await nurseA.Client.PostAsJsonAsync($"{WebEncounters}/{encounter.Id}/vital-signs", Body(DateTimeOffset.UtcNow.AddMinutes(3))), HttpStatusCode.BadRequest);
        }

        private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
        }
    }
}
