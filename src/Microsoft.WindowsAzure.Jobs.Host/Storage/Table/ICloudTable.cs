using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs.Host.Storage.Table
{
    internal interface ICloudTable
    {
        T GetOrInsert<T>(T entity) where T : TableServiceEntity;
        void InsertEntity<T>(T entity) where T : TableServiceEntity;
        IEnumerable<T> QueryByRowKeyRange<T>(string partitionKey, string rowKeyExclusiveLowerBound, string rowKeyExclusiveUpperBound, int? limit) where T : TableServiceEntity;
    }
}
