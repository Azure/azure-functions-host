using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Storage.Table
{
    internal interface ICloudTable
    {
        TElement Retrieve<TElement>(string partitionKey, string rowKey) where TElement : class, ITableEntity;

        void Insert(ITableEntity entity);

        void Insert(IEnumerable<ITableEntity> entities);

        void InsertOrReplace(ITableEntity entity);

        void InsertOrReplace(IEnumerable<ITableEntity> entities);

        void Replace(ITableEntity entity);

        void Replace(IEnumerable<ITableEntity> entities);

        IEnumerable<TElement> Query<TElement>(int? limit, params IQueryModifier[] queryModifiers) where TElement : ITableEntity, new();

        TElement GetOrInsert<TElement>(TElement entity) where TElement : ITableEntity, new();
    }
}
