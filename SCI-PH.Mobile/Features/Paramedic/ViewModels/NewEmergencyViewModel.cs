using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels
{
    public partial class NewEmergencyViewModel : ObservableObject
    {
        private readonly IParamedicApiService _api;

        public NewEmergencyViewModel(IParamedicApiService api)
        {
            _api = api;
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private string description = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private string location = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private bool isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string? errorMessage;

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        private bool CanSave() =>
            !IsBusy &&
            !string.IsNullOrWhiteSpace(Description) &&
            !string.IsNullOrWhiteSpace(Location);

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            ErrorMessage = null;
            IsBusy = true;

            try
            {
                var emergency = await _api.CreateEmergencyAsync(new CreateEmergencyDto
                {
                    Description = Description.Trim(),
                    Location = Location.Trim()
                });

                // TODO: navegar a la pantalla de Identificación pasando emergency.Id
                await Shell.Current.DisplayAlert(
                    "Emergencia creada",
                    $"ID: {emergency.Id}\nEstado: {emergency.Status}",
                    "OK");

                Description = string.Empty;
                Location = string.Empty;
            }
            catch (Exception)
            {
                // Mensaje genérico: no exponer detalles internos al usuario.
                ErrorMessage = "No se pudo crear la emergencia. Intenta nuevamente.";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
