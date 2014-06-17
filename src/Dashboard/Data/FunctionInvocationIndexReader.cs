using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Storage.Table;

namespace Dashboard.Data
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
            modifiers.Add(new PartitionKeyEqualsQueryModifier(partitionKey));
            if (newerThan != null)
            {
                modifiers.Add(new RowKeyLessThanQueryModifier(newerThan));
            }
            if (olderThan != null)
            {
                modifiers.Add(new RowKeyGreaterThanQueryModifier(olderThan));
            }

            if (olderThanOrEqual != null)
            {
                modifiers.Add(new RowKeyGreaterThanOrEqualQueryModifier(olderThanOrEqual));
            }

            var stuff = _table.Query<FunctionInvocationIndexEntity>(limit.GetValueOrDefault(10), modifiers.ToArray());

            return stuff.ToArray();
        }
    }
}