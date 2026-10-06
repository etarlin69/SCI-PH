using Microsoft.Extensions.Logging;
using SCI_PH.Mobile.Extensions;
using SCI_PH.Mobile.Features.Paramedic;

namespace SCI_PH.Mobile
{
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

#if DEBUG
    		builder.Logging.AddDebug();
#endif
            builder.Services
                .AddCoreServices() // // compartido para el compa del rol 7
                .AddParamedicFeature();

            return builder.Build();
        }
    }
}
