using Microsoft.Extensions.Logging;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;
using SCI_PH.PatientPrototype.Features.Patient.Services.Mocks;

namespace SCI_PH.PatientPrototype;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<IPatientSessionService, MockPatientSessionService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}