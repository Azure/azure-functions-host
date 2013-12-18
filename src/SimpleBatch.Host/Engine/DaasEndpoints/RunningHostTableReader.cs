using System;
using System.Linq;

namespace Microsoft.WindowsAzure.Jobs
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
