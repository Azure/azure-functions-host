using System;
using System.Linq;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class TableQueryable
    {
        public static T GetEntity<T>(IQueryable<T> queryable, string partitionKey, string rowKey) where T : TableServiceEntity
        {
            if (queryable == null)
            {
                throw new ArgumentNullException("queryable");
            }

            IQueryable<T> query = from T entity in queryable
                                  where entity.PartitionKey == partitionKey
                                  && entity.RowKey == rowKey
                                  select entity;
            return query.SingleOrDefault();
        }
    }
}
