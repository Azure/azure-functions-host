// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    /// <summary>
    /// The POCO entity writer.
    /// </summary>
    /// <typeparam name="T">The POCO type.</typeparam>
    internal class PocoEntityWriter<T> : ICollector<T>, IAsyncCollector<T>, IWatcher
    {
        internal TableEntityWriter<ITableEntity> TableEntityWriter { get; set; }

        public PocoEntityWriter(IStorageTable table, TableParameterLog tableStatistics)
        {
            TableEntityWriter = new TableEntityWriter<ITableEntity>(table, tableStatistics);
        }

        public PocoEntityWriter(IStorageTable table)
        {
            TableEntityWriter = new TableEntityWriter<ITableEntity>(table);
        }

        public void Add(T item)
        {
            AddAsync(item).GetAwaiter().GetResult();
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return TableEntityWriter.FlushAsync(cancellationToken);
        }

        public Task AddAsync(T item, CancellationToken cancellationToken = default(CancellationToken))
        {
            ITableEntity tableEntity = PocoTableEntity.ToTableEntity(item);
            return TableEntityWriter.AddAsync(tableEntity, cancellationToken);
        }

        public ParameterLog GetStatus()
        {
            return TableEntityWriter.GetStatus();
        }
    }
}
