using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos
{
    public class IdentifyPatientDto
    {
        public bool Found { get; set; }
        public Guid? PatientId { get; set; }
        public string? DisplayName { get; set; }
    }
}
