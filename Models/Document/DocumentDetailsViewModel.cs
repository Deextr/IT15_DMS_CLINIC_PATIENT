using System.Collections.Generic;
using DMS_CPMS.Data.Models;

namespace DMS_CPMS.Models.Document
{
    public class DocumentDetailsViewModel
    {
        public Data.Models.Document Document { get; set; } = default!;

        public DocumentVersion? LatestVersion { get; set; }

        public IEnumerable<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    }
}
