using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Storage.Table
{
    internal interface ICloudTable
    {
        void Insert(ITableEntity entity);

        IEnumerable<TElement> Query<TElement>(int? limit, params IQueryModifier[] queryModifiers) where TElement : ITableEntity, new();

        TElement GetOrInsert<TElement>(TElement entity) where TElement : ITableEntity, new();
    }
}
