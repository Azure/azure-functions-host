using System;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
#if PUBLICPROTOCOL
    public class RunningHostTableWriter : IRunningHostTableWriter
#else
    internal class RunningHostTableWriter : IRunningHostTableWriter
#endif
    {
        private const string PartitionKey = "1";

        private readonly ICloudTable _table;

        public RunningHostTableWriter(ICloudTableClient tableClient)
            : this(tableClient.GetTableReference(TableNames.RunningHostsTableName))
        {
        }

        public RunningHostTableWriter(ICloudTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            _table = table;
        }

        public void SignalHeartbeat(string hostName)
        {
            DynamicTableEntity entity = new DynamicTableEntity(PartitionKey, hostName);
            _table.InsertOrReplace(entity);
        }
    }
}
