using System;
using System.Diagnostics;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Table;

namespace Microsoft.WindowsAzure.Jobs.Host.Runners
{
    internal class HostTable : IHostTable
    {
        private readonly ICloudTable _table;

        public HostTable(ICloudTableClient client)
            : this(VerifyNotNull(client).GetTableReference(HostTableNames.HostsTableName))
        {
        }

        public HostTable(ICloudTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            _table = table;
        }

        public ICloudTable Table
        {
            get { return _table; }
        }

        public Guid GetOrCreateHostId(string hostName)
        {
            Debug.Assert(_table != null);
            Guid newHostId = Guid.NewGuid();

            HostEntity newEntity = new HostEntity
            {
                PartitionKey = hostName,
                RowKey = String.Empty,
                Id = newHostId
            };

            HostEntity entity = _table.GetOrInsert(newEntity);
            Debug.Assert(entity != null);
            return entity.Id;
        }

        private static ICloudTableClient VerifyNotNull(ICloudTableClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            return client;
        }
    }
}
