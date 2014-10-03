// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
    [CLSCompliant(false)]
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
