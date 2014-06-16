using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    [CLSCompliant(false)]
    public class FunctionStatisticsEntity : TableEntity
    {
        public int SucceededCount { get; set; }

        public int FailedCount { get; set; }
    }
}
