using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Storage.Table;

namespace Dashboard.Protocols
{
    internal class FunctionInvocationIndexReader  : IFunctionInvocationIndexReader
    {
        private readonly ICloudTable _table;

        public FunctionInvocationIndexReader(ICloudTableClient client, string tableName)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }
            if (tableName == null)
            {
                throw new ArgumentNullException("tableName");
            }
            _table = client.GetTableReference(tableName);
        }

        public FunctionInvocationIndexEntity[] Query(string partitionKey, string olderThan, string olderThanOrEqual, string newerThan, int? limit)
        {
            var modifiers = new List<IQueryModifier>();
            modifiers.Add(new PartitionKeyEquals(partitionKey));
            if (newerThan != null)
            {
                modifiers.Add(new RowKeyLessThan(newerThan));
            }
            if (olderThan != null)
            {
                modifiers.Add(new RowKeyGreaterThan(olderThan));
            }

            if (olderThanOrEqual != null)
            {
                modifiers.Add(new RowKeyGreaterThanOrEqual(olderThanOrEqual));
            }

            var stuff = _table.Query<FunctionInvocationIndexEntity>(limit.GetValueOrDefault(10), modifiers.ToArray());

            return stuff.ToArray();
        }
    }
}