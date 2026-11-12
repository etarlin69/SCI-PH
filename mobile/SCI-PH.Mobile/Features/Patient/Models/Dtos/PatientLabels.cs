namespace SCI_PH.Mobile.Features.Patient.Models.Dtos;

/// <summary>
/// Textos en español de los enums que llegan como texto de <c>/api/mobile/patient/me</c> (nombres de los enums del
/// servidor). Un valor que la app no conoce se muestra tal cual, nunca se inventa.
/// </summary>
public static class PatientLabels
{
    public static string Sex(string value) => Label(value, new()
    {
        ["Female"] = "Femenino",
        ["Male"] = "Masculino",
        ["Intersex"] = "Intersexual",
        ["Unknown"] = "No registrado"
    });

    public static string DocumentType(string value) => Label(value, new()
    {
        ["NationalId"] = "Cédula",
        ["Passport"] = "Pasaporte"
    });

    public static string EncounterType(string value) => Label(value, new()
    {
        ["Consultation"] = "Consulta",
        ["Emergency"] = "Emergencia",
        ["Prehospital"] = "Atención prehospitalaria",
        ["Hospitalization"] = "Hospitalización"
    });

    public static string EncounterStatus(string value) => Label(value, new()
    {
        ["InProgress"] = "En curso",
        ["Completed"] = "Finalizada",
        ["Cancelled"] = "Cancelada"
    });

    /// <summary>Atención sin clínica: la prehospitalaria antes de llegar al hospital.</summary>
    public static string NoClinic(string encounterType) =>
        encounterType == "Prehospital" ? "Ambulancia" : "Sin centro registrado";

    public static string ResultType(string value) => Label(value, new()
    {
        ["Laboratory"] = "Laboratorio",
        ["Imaging"] = "Imagen",
        ["PointOfCare"] = "Prueba en el punto de atención",
        ["Procedure"] = "Procedimiento",
        ["Other"] = "Otro"
    });

    public static string ResultStatus(string value) => Label(value, new()
    {
        ["Final"] = "Publicado",
        ["Corrected"] = "Corregido"
    });

    public static string AllergySeverity(string value) => Label(value, new()
    {
        ["Mild"] = "leve",
        ["Moderate"] = "moderada",
        ["Severe"] = "grave",
        ["Unknown"] = "desconocida"
    });

    public static string RecordStatus(string value) => Label(value, new()
    {
        ["Active"] = "Activa",
        ["Resolved"] = "Resuelta",
        ["Inactive"] = "Inactiva",
        ["Unknown"] = "Estado desconocido"
    });

    public static string DiagnosisType(string value) => Label(value, new()
    {
        ["Presumptive"] = "Presuntivo",
        ["Definitive"] = "Definitivo"
    });

    public static string Route(string value) => Label(value, new()
    {
        ["Oral"] = "Oral",
        ["Intravenous"] = "Intravenosa",
        ["Intramuscular"] = "Intramuscular",
        ["Subcutaneous"] = "Subcutánea",
        ["Inhalation"] = "Inhalada",
        ["Topical"] = "Tópica",
        ["Sublingual"] = "Sublingual",
        ["Rectal"] = "Rectal",
        ["Nasal"] = "Nasal",
        ["Ophthalmic"] = "Oftálmica",
        ["Otic"] = "Ótica",
        ["Transdermal"] = "Transdérmica",
        ["Other"] = "Otra vía"
    });

    private static string Label(string value, Dictionary<string, string> labels) =>
        labels.TryGetValue(value, out var label) ? label : value;
}
