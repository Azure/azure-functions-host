// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Auth;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Defines a table client.</summary>
#if PUBLICSTORAGE
    
    public interface IStorageTableClient
#else
    internal interface IStorageTableClient
#endif
    {
        /// <summary>Gets the credentials used to connect to the account.</summary>
        StorageCredentials Credentials { get; }

        /// <summary>Gets a table reference.</summary>
        /// <param name="tableName">The table name.</param>
        /// <returns>A table reference.</returns>
        IStorageTable GetTableReference(string tableName);
    }
}
