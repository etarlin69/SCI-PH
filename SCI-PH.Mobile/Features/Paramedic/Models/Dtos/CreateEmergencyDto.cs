using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos
{
    public class CreateEmergencyDto
    {
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }
}
