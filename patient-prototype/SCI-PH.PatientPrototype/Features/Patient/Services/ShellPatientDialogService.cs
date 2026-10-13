using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.Services;

public sealed class ShellPatientDialogService : IPatientDialogService
{
    public Task ShowAsync(string title, string message, string accept = "OK") =>
        Shell.Current.DisplayAlert(title, message, accept);
}
