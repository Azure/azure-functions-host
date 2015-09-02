// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    internal class TestJobHostConfiguration : IServiceProvider
    {
        public TestJobHostConfiguration()
        {
            StorageClientFactory = new StorageClientFactory();
        }

        public IJobHostContextFactory ContextFactory { get; set; }

        private StorageClientFactory StorageClientFactory { get; set; }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IJobHostContextFactory))
            {
                return ContextFactory;
            }
            else if (serviceType == typeof(StorageClientFactory))
            {
                return StorageClientFactory;
            }
            else
            {
                return null;
            }
        }
    }
}
