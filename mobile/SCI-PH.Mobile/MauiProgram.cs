using Microsoft.Extensions.Logging;
using SCI_PH.Mobile.Extensions;
using SCI_PH.Mobile.Features.Auth;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Patient;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

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
                })
                // ZXing: dibuja "Mi QR" (generador) y lee el QR en el escáner del paramédico (cámara). Registrar el lector no
                // abre la cámara ni pide permisos: solo la página del escáner crea la vista de cámara, tras el permiso.
                .UseBarcodeReader();

#if DEBUG
    		builder.Logging.AddDebug();
#endif
            builder.Services
                .AddCoreServices()
                .AddAuthFeature()
                .AddParamedicFeature()
                .AddPatientFeature();

            return builder.Build();
        }
    }
}
