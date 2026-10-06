using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;

namespace SCI_PH.Mobile.Features.Paramedic
{
    public static class ParamedicServiceCollectionExtensions
    {
        public static IServiceCollection AddParamedicFeature(this IServiceCollection services)
        {
            // Servicios
            #if DEBUG
                services.AddSingleton<IParamedicApiService, MockParamedicApiService>();
            #else
                services.AddHttpClient<IParamedicApiService, ParamedicApiService>();
            #endif

            // ViewModels (los iremos agregando)
            // services.AddTransient<NewEmergencyViewModel>();

            // Views (los iremos agregando)
            // services.AddTransient<NewEmergencyPage>();

            return services;
        }
    }
}
