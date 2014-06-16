using System;
using Microsoft.Azure.Jobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    public class FunctionStatisticsWriter :  IFunctionStatisticsWriter
    {
        private const string PartitionKey = "1";

        private readonly CloudTable _table;

        [CLSCompliant(false)]
        public FunctionStatisticsWriter(CloudTableClient client)
            : this(client.GetTableReference(DashboardTableNames.FunctionInvokeStatsTableName))
        {
        }

        private FunctionStatisticsWriter(CloudTable table)
        {
            _table = table;
        }

        public void IncrementSuccess(string functionId)
        {
            UpdateEntity(functionId, (e) => e.SucceededCount++);
        }

        public void IncrementFailure(string functionId)
        {
            UpdateEntity(functionId, (e) => e.FailedCount++);
        }

        private void UpdateEntity(string functionId, Action<FunctionStatisticsEntity> modifier)
        {
            // Keep racing to update the entity until it succeeds.
            while (!TryUpdateEntity(functionId, modifier));
        }

        private bool TryUpdateEntity(string functionId, Action<FunctionStatisticsEntity> modifier)
        {
            TableOperation retrieve = TableOperation.Retrieve<FunctionStatisticsEntity>(PartitionKey, functionId);
            FunctionStatisticsEntity existingEntity;

            try
            {
                existingEntity = (FunctionStatisticsEntity)_table.Execute(retrieve).Result;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    existingEntity = null;
                }
                else
                {
                    throw;
                }
            }

            TableOperation operation;

            if (existingEntity == null)
            {
                FunctionStatisticsEntity entity = new FunctionStatisticsEntity
                {
                    PartitionKey = PartitionKey,
                    RowKey = functionId,
                };
                modifier.Invoke(entity);
                operation = TableOperation.Insert(entity);
            }
            else
            {
                modifier.Invoke(existingEntity);
                operation = TableOperation.Replace(existingEntity);
            }

            try
            {
                _table.Execute(operation);
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    _table.CreateIfNotExists();

                    try
                    {
                        _table.Execute(operation);
                        return true;
                    }
                    catch (StorageException retryException)
                    {
                        if (retryException.IsPreconditionFailed() || retryException.IsConflict())
                        {
                            return false;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                else if (exception.IsPreconditionFailed() || existingEntity == null && exception.IsConflict())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
