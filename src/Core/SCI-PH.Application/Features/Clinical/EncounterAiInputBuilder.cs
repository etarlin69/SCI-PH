using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SCI_PH.Application.Abstractions.AI;
using SCI_PH.Application.DTOs.Clinical;

namespace SCI_PH.Application.Features.Clinical
{
    // A record of the encounter that the summary may reference; the alias that names it exists only inside Application.
    public sealed record EncounterAiSource(ClinicalAiSourceType Type, Guid Id);

    public sealed record EncounterAiRecordCounts(
        int Triages, int VitalSigns, int Notes, int Diagnoses, int Prescriptions, int Administrations, int Procedures, int Transfers);

    // The minimized provider input, the alias -> record map that validates and resolves the answer's sources, and the
    // fingerprint of the input (freshness, diagnostics and future deduplication; never an authentication primitive).
    public sealed record EncounterAiPreparedInput(
        EncounterSummaryInput Input,
        IReadOnlyDictionary<string, EncounterAiSource> Sources,
        string Fingerprint,
        EncounterAiRecordCounts Counts);

    // Builds the ONLY data a clinical AI provider receives for an encounter summary (ADR-013), from the encounter's own
    // records. Minimization by construction: no patient (name, identifiers, birth date, sex, contact), no clinic, no staff
    // (ids or names), no record id (aliases T1, V1, N1, D1, RX1, M1, P1, TR1) and no absolute instant (times are "+hh:mm"
    // from the encounter start). Selection is deterministic: per type the newest records up to a limit, then, while the
    // serialized input exceeds the budget, the oldest record of the first non-empty type in TrimOrder is dropped; the
    // records are then listed oldest first. Any record left out sets Truncated. Limits are server constants.
    public static class EncounterAiInputBuilder
    {
        public const int MaxTriages = 10;
        public const int MaxVitalSigns = 50;
        public const int MaxNotes = 30;
        public const int MaxDiagnoses = 20;
        public const int MaxPrescriptions = 20;
        public const int MaxAdministrations = 50;
        public const int MaxProcedures = 30;
        public const int MaxTransfers = 10;

        // Characters of the serialized input (about 15k tokens).
        public const int MaxInputCharacters = 60_000;

        private static readonly JsonSerializerOptions _canonical = new(JsonSerializerDefaults.Web);

        public static EncounterAiPreparedInput Build(
            EncounterRow encounter,
            IReadOnlyList<TriageSummary> triages,
            IReadOnlyList<VitalSignSummary> vitalSigns,
            IReadOnlyList<ClinicalNoteSummary> notes,
            IReadOnlyList<DiagnosisSummary> diagnoses,
            IReadOnlyList<PrescriptionSummary> prescriptions,
            IReadOnlyList<MedicationAdministrationSummary> administrations,
            IReadOnlyList<ClinicalProcedureSummary> procedures,
            IReadOnlyList<TransferRow> transfers)
        {
            var truncated = false;
            var selection = new Selection(
                Newest(triages, t => t.PerformedAtUtc, t => t.Id, MaxTriages, ref truncated),
                Newest(vitalSigns, v => v.MeasuredAtUtc, v => v.Id, MaxVitalSigns, ref truncated),
                Newest(notes, n => n.OccurredAtUtc, n => n.Id, MaxNotes, ref truncated),
                Newest(diagnoses, d => d.RecordedAtUtc, d => d.Id, MaxDiagnoses, ref truncated),
                Newest(prescriptions, p => p.IssuedAtUtc ?? p.CreatedAtUtc, p => p.Id, MaxPrescriptions, ref truncated),
                Newest(administrations, a => a.AdministeredAtUtc, a => a.Id, MaxAdministrations, ref truncated),
                Newest(procedures, p => p.PerformedAtUtc ?? DateTimeOffset.MinValue, p => p.Id, MaxProcedures, ref truncated),
                Newest(transfers, t => t.RequestedAtUtc, t => t.Id, MaxTransfers, ref truncated));

            var (input, sources) = Compose(encounter, selection, truncated);
            while (Serialize(input).Length > MaxInputCharacters && selection.TrimOldest())
            {
                (input, sources) = Compose(encounter, selection, truncated: true);
            }

            var counts = new EncounterAiRecordCounts(
                input.Triages.Count, input.VitalSigns.Count, input.Notes.Count, input.Diagnoses.Count, input.Prescriptions.Count,
                input.Administrations.Count, input.Procedures.Count, input.Transfers.Count);
            return new EncounterAiPreparedInput(input, sources, Fingerprint(input), counts);
        }

        // SHA-256 (hex) of the canonical serialization of the minimized input: the same records give the same value.
        public static string Fingerprint(EncounterSummaryInput input) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(input))));

        // "+hh:mm" from the encounter start ("-hh:mm" before it); hours are not wrapped at 24.
        public static string Relative(DateTimeOffset at, DateTimeOffset start)
        {
            var offset = at - start;
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var duration = offset.Duration();
            return string.Create(CultureInfo.InvariantCulture, $"{sign}{(long)duration.TotalHours:00}:{duration.Minutes:00}");
        }

        private static string Serialize(EncounterSummaryInput input) => JsonSerializer.Serialize(input, _canonical);

        // The newest `limit` records (ties by id), as a list ordered oldest first.
        private static List<T> Newest<T>(IReadOnlyList<T> records, Func<T, DateTimeOffset> at, Func<T, Guid> id, int limit, ref bool truncated)
        {
            if (records.Count > limit)
            {
                truncated = true;
            }

            return records.OrderByDescending(at).ThenBy(id).Take(limit).OrderBy(at).ThenBy(id).ToList();
        }

        private static (EncounterSummaryInput Input, IReadOnlyDictionary<string, EncounterAiSource> Sources) Compose(
            EncounterRow encounter, Selection selection, bool truncated)
        {
            var start = encounter.StartedAtUtc;
            var sources = new Dictionary<string, EncounterAiSource>(StringComparer.Ordinal);
            string Alias(string prefix, int index, ClinicalAiSourceType type, Guid id)
            {
                var alias = string.Create(CultureInfo.InvariantCulture, $"{prefix}{index + 1}");
                sources[alias] = new EncounterAiSource(type, id);
                return alias;
            }

            var triageAliases = new Dictionary<Guid, string>();
            var triages = selection.Triages.Select((t, i) =>
            {
                var alias = triageAliases[t.Id] = Alias("T", i, ClinicalAiSourceType.Triage, t.Id);
                return new TriageInput(alias, Relative(t.PerformedAtUtc, start), t.Priority.ToString(), t.Reason, t.Observations);
            }).ToList();
            var vitalSigns = selection.VitalSigns.Select((v, i) => new VitalSignInput(
                Alias("V", i, ClinicalAiSourceType.VitalSign, v.Id), Relative(v.MeasuredAtUtc, start), v.Type.ToString(), v.Value, v.Unit,
                v.TriageId is { } triageId ? triageAliases.GetValueOrDefault(triageId) : null)).ToList();
            var notes = selection.Notes.Select((n, i) => new ClinicalNoteInput(
                Alias("N", i, ClinicalAiSourceType.ClinicalNote, n.Id), Relative(n.OccurredAtUtc, start), n.Type.ToString(), n.Content)).ToList();
            var diagnoses = selection.Diagnoses.Select((d, i) => new DiagnosisInput(
                Alias("D", i, ClinicalAiSourceType.Diagnosis, d.Id), Relative(d.RecordedAtUtc, start), d.Type.ToString(), d.Description,
                d.CodeSystem, d.Code, d.IsPrimary, d.ConfirmedAtUtc.HasValue)).ToList();
            var prescriptionAliases = new Dictionary<Guid, string>();
            var prescriptions = selection.Prescriptions.Select((p, i) =>
            {
                var alias = prescriptionAliases[p.Id] = Alias("RX", i, ClinicalAiSourceType.Prescription, p.Id);
                return new PrescriptionInput(
                    alias, Relative(p.IssuedAtUtc ?? p.CreatedAtUtc, start), p.Status.ToString(),
                    p.CancelledAtUtc is { } cancelled ? Relative(cancelled, start) : null, p.Notes,
                    p.Details.Select(d => new PrescriptionLineInput(
                        d.MedicationName, d.Dose, d.DoseUnit, d.Route.ToString(), d.Frequency, d.DurationDays, d.Instructions)).ToList());
            }).ToList();
            var administrations = selection.Administrations.Select((a, i) => new MedicationAdministrationInput(
                Alias("M", i, ClinicalAiSourceType.MedicationAdministration, a.Id), Relative(a.AdministeredAtUtc, start), a.MedicationName,
                a.Dose, a.DoseUnit, a.Route.ToString(), a.Status.ToString(),
                a.PrescriptionId is { } prescriptionId ? prescriptionAliases.GetValueOrDefault(prescriptionId) : null, a.Notes)).ToList();
            var procedures = selection.Procedures.Select((p, i) => new ProcedureInput(
                Alias("P", i, ClinicalAiSourceType.ClinicalProcedure, p.Id), p.PerformedAtUtc is { } performed ? Relative(performed, start) : null,
                p.Name, p.Code, p.Status.ToString(), p.Outcome)).ToList();
            var transfers = selection.Transfers.Select((t, i) => new TransferInput(
                Alias("TR", i, ClinicalAiSourceType.ClinicalTransfer, t.Id), t.Status.ToString(), Relative(t.RequestedAtUtc, start),
                Optional(t.SentAtUtc, start), Optional(t.ReceivedAtUtc, start), Optional(t.CancelledAtUtc, start), t.Reason, t.Notes)).ToList();

            var overview = new EncounterOverviewInput(
                encounter.Type.ToString(), encounter.Status.ToString(), encounter.Reason, encounter.LatestTriagePriority?.ToString(),
                Optional(encounter.EndedAtUtc, start));
            return (new EncounterSummaryInput(overview, triages, vitalSigns, notes, diagnoses, prescriptions, administrations, procedures, transfers, truncated),
                sources);
        }

        private static string? Optional(DateTimeOffset? at, DateTimeOffset start) => at is { } value ? Relative(value, start) : null;

        // The selected records of each type (oldest first) and the order in which the budget drops them: free text first
        // (notes, triages), structured records last, documented diagnoses never before everything else.
        private sealed class Selection(
            List<TriageSummary> triages,
            List<VitalSignSummary> vitalSigns,
            List<ClinicalNoteSummary> notes,
            List<DiagnosisSummary> diagnoses,
            List<PrescriptionSummary> prescriptions,
            List<MedicationAdministrationSummary> administrations,
            List<ClinicalProcedureSummary> procedures,
            List<TransferRow> transfers)
        {
            public List<TriageSummary> Triages { get; } = triages;
            public List<VitalSignSummary> VitalSigns { get; } = vitalSigns;
            public List<ClinicalNoteSummary> Notes { get; } = notes;
            public List<DiagnosisSummary> Diagnoses { get; } = diagnoses;
            public List<PrescriptionSummary> Prescriptions { get; } = prescriptions;
            public List<MedicationAdministrationSummary> Administrations { get; } = administrations;
            public List<ClinicalProcedureSummary> Procedures { get; } = procedures;
            public List<TransferRow> Transfers { get; } = transfers;

            private IEnumerable<System.Collections.IList> TrimOrder =>
                [Notes, Triages, Administrations, Procedures, Transfers, Prescriptions, VitalSigns, Diagnoses];

            // Drops the oldest record of the first non-empty type; false when nothing is left to drop.
            public bool TrimOldest()
            {
                var list = TrimOrder.FirstOrDefault(l => l.Count > 0);
                list?.RemoveAt(0);
                return list is not null;
            }
        }
    }
}
