using Microsoft.Extensions.Logging;

namespace SCI_PH.Mobile.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            string bffUrl = "https://localhost:5001/";

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("materialdesignicons-webfont", "MaterialIcons");
                });


#if DEBUG
    		builder.Logging.AddDebug();
#endif
            builder.Services
                .AddInfrastructureServices(bffUrl)
                .AddViewModels()
                .AddViews();

            return builder.Build();
        }
    }
}
