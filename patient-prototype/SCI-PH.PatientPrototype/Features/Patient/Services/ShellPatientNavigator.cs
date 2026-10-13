using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.Services;

public sealed class ShellPatientNavigator : IPatientNavigator
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);
}
