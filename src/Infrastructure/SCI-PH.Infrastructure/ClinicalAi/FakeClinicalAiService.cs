using System.Globalization;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Abstractions.AI;
using Answer = SCI_PH.Infrastructure.ClinicalAi.EncounterSummaryPromptV1.Answer;
using AnswerReviewPoint = SCI_PH.Infrastructure.ClinicalAi.EncounterSummaryPromptV1.AnswerReviewPoint;
using AnswerSection = SCI_PH.Infrastructure.ClinicalAi.EncounterSummaryPromptV1.AnswerSection;
using AnswerSections = SCI_PH.Infrastructure.ClinicalAi.EncounterSummaryPromptV1.AnswerSections;
using AnswerSource = SCI_PH.Infrastructure.ClinicalAi.EncounterSummaryPromptV1.AnswerSource;

namespace SCI_PH.Infrastructure.ClinicalAi
{
    // Deterministic clinical AI provider for CI, integration tests, the end-to-end of PR 1 and development (ADR-013). It is
    // NOT a model: it runs in process (no network, keys or model files) and writes a fixed-template summary from the
    // structured input only. It never copies narrative text (notes, reasons, observations, outcomes), so a note can never
    // change its answer; it quotes documented diagnoses as documented, states recorded values and numeric changes, never
    // a judgment; its only review points are documentation
    // facts derivable from the structured records. Its answer goes through the same wire document and parser as a real
    // provider's (EncounterSummaryPromptV1.ParseDraft). Configured failure scenarios (ClinicalAi:Fake) exercise the error
    // paths. Never allowed in Production (AddClinicalAi).
    public sealed class FakeClinicalAiService : IClinicalAiService
    {
        public const string ProviderName = "SCIPH-AiFake";
        public const string Model = "fake-summary-v1";

        private static readonly IReadOnlyDictionary<string, string> _vitalLabels = new Dictionary<string, string>
        {
            ["HeartRate"] = "FC",
            ["RespiratoryRate"] = "FR",
            ["SystolicBloodPressure"] = "PA sistólica",
            ["DiastolicBloodPressure"] = "PA diastólica",
            ["Temperature"] = "Temperatura",
            ["OxygenSaturation"] = "SpO2",
            ["Weight"] = "Peso",
            ["Height"] = "Talla",
            ["BloodGlucose"] = "Glucemia",
            ["PainScore"] = "Dolor (escala)",
            ["GlasgowComaScale"] = "Glasgow"
        };

        private readonly ClinicalAiOptions _options;

        public FakeClinicalAiService(IOptions<ClinicalAiOptions> options) => _options = options.Value;

        public string Provider => ProviderName;

        public string ModelVersion => Model;

        public string PromptVersion => EncounterSummaryPromptV1.Version;

        public TimeSpan Timeout => TimeSpan.FromSeconds(_options.TimeoutSeconds);

        public async Task<EncounterSummaryDraft> SummarizeEncounterAsync(EncounterSummaryInput input, CancellationToken cancellationToken)
        {
            if (_options.Fake.DelayMilliseconds > 0)
            {
                await Task.Delay(_options.Fake.DelayMilliseconds, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var answer = Summarize(input);
            var document = _options.Fake.Scenario switch
            {
                FakeClinicalAiScenario.ProviderUnavailable => throw new ClinicalAiProviderException("The fake provider is configured to fail."),
                FakeClinicalAiScenario.Timeout => await NeverAsync(cancellationToken),
                FakeClinicalAiScenario.InvalidSchema => """{"summary": 42, "sections": "none"}""",
                FakeClinicalAiScenario.UnknownAlias => EncounterSummaryPromptV1.Write(answer with
                {
                    ReviewPoints = [.. answer.ReviewPoints ?? [], new AnswerReviewPoint("PendingItem", "Registro inexistente.", [new AnswerSource("VitalSign", "V999")])]
                }),
                FakeClinicalAiScenario.OversizedOutput => EncounterSummaryPromptV1.Write(answer with { Summary = new string('x', 1501) }),
                _ => EncounterSummaryPromptV1.Write(answer)
            };

            return EncounterSummaryPromptV1.ParseDraft(document, Model);
        }

        private static async Task<string> NeverAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }

        // The fixed template, from the structured records only.
        internal static Answer Summarize(EncounterSummaryInput input)
        {
            var encounter = input.Encounter;
            var summary = Clip(
                [
                    $"Atención de tipo {encounter.Type} en estado {encounter.Status}.",
                    encounter.Reason is null ? "Motivo de la atención no documentado." : "Motivo de la atención documentado.",
                    encounter.LatestTriagePriority is { } priority ? $"Última prioridad de triaje registrada: {priority}." : "Sin prioridad de triaje registrada.",
                    $"Registros incluidos: {input.Triages.Count} triajes, {input.VitalSigns.Count} signos vitales, {input.Notes.Count} notas, "
                        + $"{input.Diagnoses.Count} diagnósticos, {input.Prescriptions.Count} prescripciones, {input.Administrations.Count} administraciones, "
                        + $"{input.Procedures.Count} procedimientos y {input.Transfers.Count} traslados.",
                    encounter.EndedAt is { } ended ? $"La atención finalizó a {ended}." : "La atención no tiene hora de finalización registrada.",
                    input.Truncated ? "Solo se incluyeron los registros más recientes (entrada recortada)." : "Se incluyeron todos los registros de la atención."
                ],
                1500);

            return new Answer(
                summary,
                new AnswerSections(Course(input), VitalSigns(input), Medications(input), Procedures(input), Notes(input), Transfer(input)),
                ReviewPoints(input),
                NotDocumented(input));
        }

        private static AnswerSection? Course(EncounterSummaryInput input)
        {
            if (input.Triages.Count == 0 && input.Diagnoses.Count == 0)
            {
                return null;
            }

            var parts = input.Triages.Select(t => $"Triaje con prioridad {t.Priority} a {t.At}.")
                .Concat(input.Diagnoses.Select(d => $"Se documentó el diagnóstico \"{d.Description}\" ({d.Type}{(d.IsPrimary ? ", principal" : "")}) a {d.At}."));
            return new AnswerSection(
                Clip(parts, 800),
                [.. input.Triages.Select(t => Source("Triage", t.Alias)), .. input.Diagnoses.Select(d => Source("Diagnosis", d.Alias))]);
        }

        // Per type: the recorded value, or its first and last values as a numeric change.
        private static AnswerSection? VitalSigns(EncounterSummaryInput input)
        {
            if (input.VitalSigns.Count == 0)
            {
                return null;
            }

            var parts = new List<string>();
            var sources = new List<AnswerSource?>();
            foreach (var group in input.VitalSigns.GroupBy(v => v.Type).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var first = group.First();
                var last = group.Last();
                var label = _vitalLabels.GetValueOrDefault(group.Key, group.Key);
                parts.Add(group.Count() == 1
                    ? $"Se registró {label} de {Number(first.Value)} {first.Unit} a {first.At}."
                    : $"{label} cambió de {Number(first.Value)} a {Number(last.Value)} {last.Unit} entre {first.At} y {last.At} ({group.Count()} registros).");
                sources.Add(Source("VitalSign", first.Alias));
                if (!ReferenceEquals(first, last))
                {
                    sources.Add(Source("VitalSign", last.Alias));
                }
            }

            return new AnswerSection(Clip(parts, 800), sources.Take(20).ToList());
        }

        private static AnswerSection? Medications(EncounterSummaryInput input)
        {
            if (input.Prescriptions.Count == 0 && input.Administrations.Count == 0)
            {
                return null;
            }

            var parts = input.Administrations.Select(a => $"Se registró la administración de {a.MedicationName} {Number(a.Dose)} {a.DoseUnit} ({a.Route}, {a.Status}) a {a.At}.")
                .Concat(input.Prescriptions.Select(p => $"Prescripción en estado {p.Status} a {p.At} con {p.Lines.Count} línea(s): "
                    + string.Join("; ", p.Lines.Select(l => $"{l.MedicationName} {Number(l.Dose)} {l.DoseUnit} {l.Frequency}")) + "."));
            return new AnswerSection(
                Clip(parts, 800),
                input.Administrations.Select(a => Source("MedicationAdministration", a.Alias))
                    .Concat(input.Prescriptions.Select(p => Source("Prescription", p.Alias)))
                    .Take(20).ToList<AnswerSource?>());
        }

        private static AnswerSection? Procedures(EncounterSummaryInput input) =>
            input.Procedures.Count == 0
                ? null
                : new AnswerSection(
                    Clip(input.Procedures.Select(p => $"Procedimiento {p.Name} en estado {p.Status}{(p.At is { } at ? $" a {at}" : "")}."), 800),
                    input.Procedures.Take(20).Select(p => Source("ClinicalProcedure", p.Alias)).ToList());

        // Counts and types only: the content of a note is never copied.
        private static AnswerSection? Notes(EncounterSummaryInput input) =>
            input.Notes.Count == 0
                ? null
                : new AnswerSection(
                    Clip(
                        [
                            $"{input.Notes.Count} nota(s) clínica(s) documentada(s) entre {input.Notes[0].At} y {input.Notes[^1].At}.",
                            "Tipos: " + string.Join(", ", input.Notes.GroupBy(n => n.Type).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} ({g.Count()})")) + "."
                        ],
                        800),
                    input.Notes.TakeLast(20).Select(n => Source("ClinicalNote", n.Alias)).ToList());

        private static AnswerSection? Transfer(EncounterSummaryInput input) =>
            input.Transfers.Count == 0
                ? null
                : new AnswerSection(
                    Clip(input.Transfers.Select(t => $"Traslado en estado {t.Status}, solicitado a {t.RequestedAt}"
                        + (t.SentAt is { } sent ? $", enviado a {sent}" : "")
                        + (t.ReceivedAt is { } received ? $", recibido a {received}" : "")
                        + (t.CancelledAt is { } cancelled ? $", cancelado a {cancelled}" : "") + "."), 800),
                    input.Transfers.Take(20).Select(t => Source("ClinicalTransfer", t.Alias)).ToList());

        // Documentation facts only, derived from the structured records.
        private static List<AnswerReviewPoint?> ReviewPoints(EncounterSummaryInput input)
        {
            var points = new List<AnswerReviewPoint?>();
            var administered = input.Administrations.Where(a => a.PrescriptionAlias is not null).Select(a => a.PrescriptionAlias!).ToHashSet(StringComparer.Ordinal);
            foreach (var prescription in input.Prescriptions.Where(p => p.Status == "Issued" && !administered.Contains(p.Alias)))
            {
                points.Add(new AnswerReviewPoint(
                    "PendingItem", $"Prescripción emitida a {prescription.At} sin administración documentada vinculada.", [Source("Prescription", prescription.Alias)]));
            }

            if (input.VitalSigns.Count == 0)
            {
                points.Add(new AnswerReviewPoint("MissingDocumentation", "No hay signos vitales documentados en la atención.", []));
            }

            if (input.Notes.Count == 0)
            {
                points.Add(new AnswerReviewPoint("MissingDocumentation", "No hay notas clínicas documentadas en la atención.", []));
            }

            if (input.Encounter.Status is "Closed" or "Cancelled")
            {
                foreach (var transfer in input.Transfers.Where(t => t.Status is "Requested" or "InTransit"))
                {
                    points.Add(new AnswerReviewPoint(
                        "Inconsistency", $"La atención figura como {input.Encounter.Status} con un traslado en estado {transfer.Status}.",
                        [Source("ClinicalTransfer", transfer.Alias)]));
                }
            }

            return points.Take(10).ToList();
        }

        private static List<string> NotDocumented(EncounterSummaryInput input)
        {
            var missing = new List<string>();
            if (input.Encounter.Reason is null) missing.Add("Motivo de la atención");
            if (input.Triages.Count == 0) missing.Add("Triaje");
            if (input.VitalSigns.Count == 0) missing.Add("Signos vitales");
            if (input.Notes.Count == 0) missing.Add("Notas clínicas");
            if (input.Diagnoses.Count == 0) missing.Add("Diagnósticos");
            if (input.Prescriptions.Count == 0 && input.Administrations.Count == 0) missing.Add("Medicación");
            if (input.Procedures.Count == 0) missing.Add("Procedimientos");
            if (input.Transfers.Count == 0) missing.Add("Traslados");
            return missing.Take(10).ToList();
        }

        private static AnswerSource Source(string type, string alias) => new(type, alias);

        private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        // Whole sentences up to the limit (the first one cut if it alone is longer).
        private static string Clip(IEnumerable<string> sentences, int maxLength)
        {
            var text = string.Empty;
            foreach (var sentence in sentences)
            {
                var next = text.Length == 0 ? sentence : $"{text} {sentence}";
                if (next.Length > maxLength)
                {
                    return text.Length == 0 ? sentence[..maxLength] : text;
                }

                text = next;
            }

            return text;
        }
    }
}
