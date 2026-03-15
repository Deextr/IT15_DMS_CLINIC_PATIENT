using System;
using System.Collections.Generic;
using DMS_CPMS.Data.Models;

namespace DMS_CPMS.Models.Patient
{
    public class PatientDetailsViewModel
    {
        public Data.Models.Patient Patient { get; set; } = default!;

        public IEnumerable<DocumentSummaryViewModel> Documents { get; set; } = new List<DocumentSummaryViewModel>();
    }

    public class DocumentSummaryViewModel
    {
        public int DocumentID { get; set; }

        public string DocumentTitle { get; set; } = string.Empty;

        public string DocumentType { get; set; } = string.Empty;

        public DateTime UploadDate { get; set; }
    }
}
