using System;
using System.Linq;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;

namespace Dashboard.Protocols
{
    internal class RunningHostTableReader : IRunningHostTableReader
    {
        private readonly IAzureTable<RunningHost> _table;

        public RunningHostTableReader(IAzureTable<RunningHost> table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            _table = table;
        }

        public RunningHost[] ReadAll()
        {
            return _table.Enumerate(RunningHostTableWriter.PartitionKey).ToArray();
        }
    }
}
