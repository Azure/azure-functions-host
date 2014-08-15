// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Queues;

namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    internal class TestJobHostConfiguration : IServiceProvider
    {
        public IStorageAccountProvider StorageAccountProvider { get; set; }

        public IConnectionStringProvider ConnectionStringProvider { get; set; }

        public IStorageCredentialsValidator StorageCredentialsValidator { get; set; }

        public ITypeLocator TypeLocator { get; set; }

        public IQueueConfiguration Queues { get; set; }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IStorageAccountProvider))
            {
                return StorageAccountProvider;
            }
            else if (serviceType == typeof(IStorageCredentialsValidator))
            {
                return StorageCredentialsValidator;
            }
            else if (serviceType == typeof(IConnectionStringProvider))
            {
                return ConnectionStringProvider;
            }
            else if (serviceType == typeof(ITypeLocator))
            {
                return TypeLocator;
            }
            else if (serviceType == typeof(IQueueConfiguration))
            {
                return Queues;
            }
            else
            {
                return null;
            }
        }
    }
}
