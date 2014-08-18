// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class NullConnectionStringProvider : IConnectionStringProvider
    {
        public string GetConnectionString(string connectionStringName)
        {
            return null;
        }
    }
}
