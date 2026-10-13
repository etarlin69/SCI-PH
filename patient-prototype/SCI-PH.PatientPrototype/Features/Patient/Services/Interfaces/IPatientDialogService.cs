namespace SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

/// <summary>Avisos al paciente desde los ViewModels.</summary>
public interface IPatientDialogService
{
    Task ShowAsync(string title, string message, string accept = "OK");
}
