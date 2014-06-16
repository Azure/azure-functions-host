using System;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    public class FunctionStatisticsReader :  IFunctionStatisticsReader
    {
        private const string PartitionKey = "1";

        private readonly CloudTable _table;

        [CLSCompliant(false)]
        public FunctionStatisticsReader(CloudTableClient client)
            : this(client.GetTableReference(DashboardTableNames.FunctionInvokeStatsTableName))
        {
        }

        private FunctionStatisticsReader(CloudTable table)
        {
            _table = table;
        }

        [CLSCompliant(false)]
        public FunctionStatisticsEntity Lookup(string functionId)
        {
            TableOperation retrieve = TableOperation.Retrieve<FunctionStatisticsEntity>(PartitionKey, functionId);

            try
            {
                return (FunctionStatisticsEntity)_table.Execute(retrieve).Result;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
