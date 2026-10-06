using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos
{
    public class RegisterVitalsDto
    {
        public int HeartRate { get; set; }          // lpm
        public int RespiratoryRate { get; set; }    // rpm
        public int SystolicBp { get; set; }         // mmHg
        public int DiastolicBp { get; set; }        // mmHg
        public int OxygenSaturation { get; set; }   // %
        public decimal Temperature { get; set; }    // °C
        public DateTime MeasuredAt { get; set; }
    }
}
