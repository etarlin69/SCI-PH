using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Views;

namespace SCI_PH.Mobile.Features.Paramedic
{
    public static class ParamedicServiceCollectionExtensions
    {
        public static IServiceCollection AddParamedicFeature(this IServiceCollection services)
        {
            // Servicios (por ahora siempre el mock)
            services.AddSingleton<IParamedicApiService, MockParamedicApiService>();

            // ViewModels
            services.AddTransient<NewEmergencyViewModel>();

            // Views
            services.AddTransient<NewEmergencyPage>();

            return services;
        }
    }
}
