using Microsoft.Extensions.Logging;
using SCI_PH.PatientPrototype.Features.Patient;

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

        builder.Services.AddPatientFeature();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
