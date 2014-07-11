using System;

namespace Dashboard.Data.Logs
{
    public class IndexerLogEntry 
    {
        public string Id { get; set; }

        public DateTime Date { get; set; }

        public string Title { get; set; }

        public string ExceptionDetails { get; set; }
    }
}