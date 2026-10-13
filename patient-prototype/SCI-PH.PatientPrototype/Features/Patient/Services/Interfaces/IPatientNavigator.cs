namespace SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

/// <summary>Navegación del módulo para los ViewModels, sin que dependan de <c>Shell.Current</c>.</summary>
public interface IPatientNavigator
{
    /// <summary>Navega a una ruta de <see cref="PatientRoutes"/> (relativa o absoluta).</summary>
    Task GoToAsync(string route);
}
