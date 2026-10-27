using SCI_PH.Application.DTOs.Clinical;

namespace SCI_PH.Application.Features.Clinical
{
    // Fixed, server-controlled catalog of the procedures a paramedic records from the mobile app (ADR-008, decision 8).
    // An internal SCI-PH prototype terminology, not free text and not yet an administered catalog: it can later be
    // replaced by a managed catalog or a standard terminology under another code system. Codes are stable: a recorded
    // ClinicalProcedure keeps CodeSystem + Code, so an entry is never renamed to mean something else, only added.
    // Codes are upper case because ClinicalProcedure stores codes upper case.
    public static class PrehospitalProcedureCatalog
    {
        public const string CodeSystem = "SCI-PH-PREHOSP";

        public static IReadOnlyList<PrehospitalProcedureCatalogItem> Entries { get; } =
        [
            new("IMMOBILIZATION", "Inmovilización",
                "Inmovilización de columna o de extremidades para el traslado."),
            new("HEMORRHAGE-CONTROL", "Control de hemorragia",
                "Compresión directa, vendaje compresivo o torniquete."),
            new("OXYGEN-THERAPY", "Oxigenoterapia",
                "Administración de oxígeno suplementario."),
            new("PERIPHERAL-IV-ACCESS", "Acceso venoso periférico",
                "Canalización de una vía venosa periférica."),
            new("CPR", "Reanimación cardiopulmonar",
                "Compresiones torácicas y ventilación.")
        ];

        // Exact, case-sensitive match: the client sends the code exactly as the catalog lists it.
        public static PrehospitalProcedureCatalogItem? Find(string code) =>
            Entries.SingleOrDefault(e => string.Equals(e.Code, code, StringComparison.Ordinal));
    }
}
