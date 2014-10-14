// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal interface IStorageAccountProvider
    {
        Task<IStorageAccount> GetAccountAsync(string connectionStringName, CancellationToken cancellationToken);
    }
}
