using System;
using System.Linq;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Protocols
{
    internal class RunningHostTableReader : IRunningHostTableReader
    {
        private const string PartitionKey = "1";

        private readonly ICloudTable _table;

        public RunningHostTableReader(ICloudTableClient tableClient)
            : this(tableClient.GetTableReference(TableNames.RunningHostsTableName))
        {
        }

        public RunningHostTableReader(ICloudTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            _table = table;
        }

        public RunningHost[] ReadAll()
        {
            return _table.Query<RunningHost>(limit: null).ToArray();
        }

        public DateTimeOffset? Read(Guid hostOrInstanceId)
        {
            RunningHost entity = _table.Retrieve<RunningHost>(PartitionKey, hostOrInstanceId.ToString());

            if (entity == null)
            {
                return null;
            }

            return entity.Timestamp;
        }
    }
}
