using System.Collections.Generic;
using DMS_CPMS.Data.Models;

namespace DMS_CPMS.Models.Patient
{
    public class PatientIndexViewModel
    {
        public IEnumerable<Data.Models.Patient> Patients { get; set; } = new List<Data.Models.Patient>();

        public string? SearchTerm { get; set; }

        public string? GenderFilter { get; set; }

        public int PageNumber { get; set; }

        public int TotalPages { get; set; }

        // For the create patient modal on the index page
        public CreatePatientViewModel NewPatient { get; set; } = new CreatePatientViewModel();
    }
}

