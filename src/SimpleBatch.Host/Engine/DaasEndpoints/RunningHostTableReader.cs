using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RunnerInterfaces;
using SimpleBatch;

namespace DaasEndpoints
{
    public class RunningHostTableReader : IRunningHostTableReader
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
