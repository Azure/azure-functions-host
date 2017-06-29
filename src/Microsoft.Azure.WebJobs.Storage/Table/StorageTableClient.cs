// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Represents a table client.</summary>
#if PUBLICSTORAGE
    
    public class StorageTableClient : IStorageTableClient
#else
    internal class StorageTableClient : IStorageTableClient
#endif
    {
        private readonly CloudTableClient _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageTableClient"/> class.</summary>
        /// <param name="sdk">The SDK client to wrap.</param>
        public StorageTableClient(CloudTableClient sdk)
        {
            _sdk = sdk;
        }

        /// <inheritdoc />
        public StorageCredentials Credentials
        {
            get { return _sdk.Credentials; }
        }

        /// <inheritdoc />
        public IStorageTable GetTableReference(string tableName)
        {
            CloudTable sdkTable = _sdk.GetTableReference(tableName);
            return new StorageTable(sdkTable);
        }
    }
}
