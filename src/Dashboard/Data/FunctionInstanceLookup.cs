using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    internal class FunctionInstanceLookup : IFunctionInstanceLookup
    {
        private const string PartitionKey = "1";

        private readonly ICloudTable _table;

        public FunctionInstanceLookup(ICloudTableClient tableClient)
            : this(tableClient.GetTableReference(DashboardTableNames.FunctionInvokeLogTableName))
        {
        }

        public FunctionInstanceLookup(ICloudTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }

            _table = table;
        }

        FunctionInstanceSnapshot IFunctionInstanceLookup.Lookup(Guid id)
        {
            FunctionInstanceEntityGroup group = FunctionInstanceEntityGroup.Lookup(_table, id);

            if (group == null)
            {
                return null;
            }

            return new FunctionInstanceSnapshot(group.InstanceEntity, group.ArgumentEntities);
        }
   }
}
