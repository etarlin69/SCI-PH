using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SCI_PH.Mobile.Features.Paramedic.Models.enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models
{
    public class EmergencyDTO
    {
        public Guid Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public EmergencyStatus Status { get; set; }
        public Guid? PatientId { get; set; }
    }
}
