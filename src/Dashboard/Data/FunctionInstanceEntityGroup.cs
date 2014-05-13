using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    [CLSCompliant(false)]
    public class FunctionInstanceEntityGroup
    {
        private const string PartitionKey = "1";

        public FunctionInstanceEntity InstanceEntity { get; set; }

        public IReadOnlyList<FunctionArgumentEntity> ArgumentEntities { get; set; }

        public IEnumerable<ITableEntity> GetEntities()
        {
            List<ITableEntity> entites = new List<ITableEntity>();
            entites.Add(InstanceEntity);
            entites.AddRange(ArgumentEntities);
            return entites;
        }

        internal static FunctionInstanceEntityGroup Lookup(ICloudTable table, Guid id)
        {
            IEnumerable<TableEntity> entities = table.QueryByRowKeyPrefix<TableEntity>(PartitionKey, id.ToString(),
                Resolve);

            FunctionInstanceEntity instanceEntity = null;
            List<FunctionArgumentEntity> argumentEntities = new List<FunctionArgumentEntity>();

            foreach (TableEntity entity in entities)
            {
                FunctionInstanceEntity possibleInstanceEntity = entity as FunctionInstanceEntity;

                if (possibleInstanceEntity != null)
                {
                    if (instanceEntity != null)
                    {
                        throw new InvalidOperationException("Multiple instance entities.");
                    }

                    instanceEntity = possibleInstanceEntity;
                    continue;
                }

                FunctionArgumentEntity argumentEntity = entity as FunctionArgumentEntity;

                if (argumentEntity != null)
                {
                    argumentEntities.Add(argumentEntity);
                    continue;
                }

                throw new InvalidOperationException("Unknown entity type");
            }

            if (instanceEntity == null)
            {
                if  (argumentEntities.Count > 0)
                {
                    throw new InvalidOperationException("Argument entities exist without instance entity.");
                }

                return null;
            }
            else
            {
                return new FunctionInstanceEntityGroup
                {
                    InstanceEntity = instanceEntity,
                    ArgumentEntities = argumentEntities
                };
            }
        }

        private static TableEntity Resolve(string partitionKey, string rowKey, DateTimeOffset timestamp,
            IDictionary<string, EntityProperty> properties, string etag)
        {
            TableEntity entity;

            if (rowKey.Contains("_Argument_"))
            {
                entity = new FunctionArgumentEntity();
            }
            else
            {
                entity = new FunctionInstanceEntity();
            }

            entity.PartitionKey = partitionKey;
            entity.RowKey = rowKey;
            entity.Timestamp = timestamp;
            entity.ReadEntity(properties, null);
            entity.ETag = etag;
            return entity;
        }
    }
}
