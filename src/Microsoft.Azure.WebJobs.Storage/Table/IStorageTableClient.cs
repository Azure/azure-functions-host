// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Defines a table client.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageTableClient
#else
    internal interface IStorageTableClient
#endif
    {
        /// <summary>Gets a table reference.</summary>
        /// <param name="tableName">The table name.</param>
        /// <returns>A table reference.</returns>
        IStorageTable GetTableReference(string tableName);
    }
}
