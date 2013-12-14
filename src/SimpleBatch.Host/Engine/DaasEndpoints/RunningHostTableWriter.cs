using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace Microsoft.WindowsAzure.Jobs
{
    public class RunningHostTableWriter : IRunningHostTableWriter
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

        public void SignalHeartbeat(string hostName)
        {
            _table.Write(PartitionKey, hostName, null);
            _table.Flush();
        }
    }
}
