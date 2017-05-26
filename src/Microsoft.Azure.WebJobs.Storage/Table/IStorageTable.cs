// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Defines a table.</summary>
#if PUBLICSTORAGE
    
    public interface IStorageTable
#else
    internal interface IStorageTable
#endif
    {
        /// <summary>Gets the name of the table.</summary>
        string Name { get; }

        /// <summary>Gets the underlying <see cref="CloudTable"/>.</summary>
        CloudTable SdkObject { get; }

        /// <summary>Creates a batch operation.</summary>
        /// <returns>A new batch operation.</returns>
        IStorageTableBatchOperation CreateBatch();

        /// <summary>Creates the table if it does not already exist.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will create the table if it does not already exist.</returns>
        Task CreateIfNotExistsAsync(CancellationToken cancellationToken);

        /// <summary>Creates an operation to insert an entity.</summary>
        /// <param name="entity">The entity to insert.</param>
        /// <returns>An operation to insert an entity.</returns>
        IStorageTableOperation CreateInsertOperation(ITableEntity entity);

        /// <summary>Creates an operation to insert or replace an entity.</summary>
        /// <param name="entity">The entity to insert or replace.</param>
        /// <returns>An operation to insert or replace an entity.</returns>
        IStorageTableOperation CreateInsertOrReplaceOperation(ITableEntity entity);

        /// <summary>Creates an operation to replace an entity.</summary>
        /// <param name="entity">The entity to replace.</param>
        /// <returns>An operation to replace an entity.</returns>
        IStorageTableOperation CreateReplaceOperation(ITableEntity entity);

        /// <summary>Creates an operation to retrieve an entity.</summary>
        /// <typeparam name="TElement">The type of entity to retrieve.</typeparam>
        /// <param name="partitionKey">The partition key of the entity to retrieve.</param>
        /// <param name="rowKey">The row key of the entity to retrieve.</param>
        /// <returns>An operation to retrieve an entity.</returns>
        IStorageTableOperation CreateRetrieveOperation<TElement>(string partitionKey, string rowKey)
            where TElement : ITableEntity, new();

        /// <summary>Executes an operation.</summary>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will execute an operation and return the result.</returns>
        Task<TableResult> ExecuteAsync(IStorageTableOperation operation, CancellationToken cancellationToken);

        /// <summary>Executes an atomic batch operation.</summary>
        /// <param name="batch">The batch operation to execute.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will execute an atomic batch operation and return the results.</returns>
        Task<IList<TableResult>> ExecuteBatchAsync(IStorageTableBatchOperation batch,
            CancellationToken cancellationToken);

        /// <summary>Determines whether the table exists.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will determine whether the table exists.</returns>
        Task<bool> ExistsAsync(CancellationToken cancellationToken);
    }
}
