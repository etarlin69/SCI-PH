using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SCI_PH.Mobile.Features.Auth.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views
{
    public partial class NewEmergencyPage : ContentPage
    {
        public NewEmergencyPage(NewEmergencyViewModel viewModel, ClinicalAccountViewModel account)
        {
            InitializeComponent();
            BindingContext = viewModel;
            LogoutToolbarItem.Command = account.LogoutCommand;
        }
    }
}
