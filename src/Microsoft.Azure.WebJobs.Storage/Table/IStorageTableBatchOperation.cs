// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Defines an atomic batch operation on a table.</summary>
#if PUBLICSTORAGE
    
    public interface IStorageTableBatchOperation : IReadOnlyList<IStorageTableOperation>
#else
    internal interface IStorageTableBatchOperation : IReadOnlyList<IStorageTableOperation>
#endif
    {
        /// <summary>Adds an operation to the batch.</summary>
        /// <param name="operation">The operation to add.</param>
        void Add(IStorageTableOperation operation);
    }
}
