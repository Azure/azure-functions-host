// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Defines an operation on a table.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageTableOperation
#else
    internal interface IStorageTableOperation
#endif
    {
        /// <summary>Gets the type of operation to perform.</summary>
        /// <remarks>
        /// When <see cref="OperationType"/> is <see cref="TableOperationType.Retrieve"/>, returns
        /// <see langword="null"/>.
        /// </remarks>
        TableOperationType OperationType { get; }

        /// <summary>Gets the entity on which to operate.</summary>
        /// <remarks>
        /// When <see cref="OperationType"/> is <see cref="TableOperationType.Retrieve"/>, returns
        /// <see langword="null"/>.
        /// </remarks>
        ITableEntity Entity { get; }

        /// <summary>Gets the partition key of the entity to retrieve.</summary>
        /// <remarks>
        /// When <see cref="OperationType"/> is not <see cref="TableOperationType.Retrieve"/>, returns
        /// <see langword="null"/>.
        /// </remarks>
        string RetrievePartitionKey { get; }

        /// <summary>Gets the row key of the entity to retrieve.</summary>
        /// <remarks>
        /// When <see cref="OperationType"/> is not <see cref="TableOperationType.Retrieve"/>, returns
        /// <see langword="null"/>.
        /// </remarks>
        string RetrieveRowKey { get; }

        /// <summary>Gets the resolver to resolve the entity to retrieve.</summary>
        /// <remarks>
        /// When <see cref="OperationType"/> is not <see cref="TableOperationType.Retrieve"/>, returns
        /// <see langword="null"/>.
        /// </remarks>
        IEntityResolver RetrieveEntityResolver { get; }
    }
}
