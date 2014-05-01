using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Storage.Table
{
    internal interface ICloudTable
    {
        T GetOrInsert<T>(T entity) where T : ITableEntity, new();
        void InsertEntity<T>(T entity) where T : ITableEntity;
        IEnumerable<T> Query<T>(int limit, params IQueryModifier[] queryModifiers) where T : ITableEntity, new();
    }
}
