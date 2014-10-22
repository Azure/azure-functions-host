// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal interface IStorageAccountParser
    {
        IStorageAccount ParseAccount(string connectionString, string connectionStringName);
    }
}
