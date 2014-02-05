using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class RunningHostTableWriter : IRunningHostTableWriter
    {
        internal const string PartitionKey = "1";

        private readonly IAzureTable<RunningHost> _table;

        public RunningHostTableWriter(IAzureTable<RunningHost> table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            _table = table;
        }

        public void SignalHeartbeat(Guid hostId)
        {
            _table.Write(PartitionKey, hostId.ToString(), null);
            _table.Flush();
        }
    }
}
