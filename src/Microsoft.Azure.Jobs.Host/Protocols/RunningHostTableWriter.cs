using System;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class RunningHostTableWriter : IRunningHostTableWriter
    {
        internal const string PartitionKey = "1";

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

        public void SignalHeartbeat(Guid hostOrInstanceId)
        {
            DynamicTableEntity entity = new DynamicTableEntity(PartitionKey, hostOrInstanceId.ToString());
            _table.InsertOrReplace(entity);
        }
    }
}
