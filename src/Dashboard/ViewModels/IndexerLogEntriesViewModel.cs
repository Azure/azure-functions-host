using System.Collections.Generic;
using Dashboard.Data.Logs;

namespace Dashboard.ViewModels
{
    public class IndexerLogEntriesViewModel
    {
        public IEnumerable<IndexerLogEntry> Entries { get; set; }

        public string ContinuationToken { get; set; }
    }
}