// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    internal class TestJobHostConfiguration : IServiceProvider
    {
        public IStorageAccountProvider StorageAccountProvider { get; set; }

        public IConnectionStringProvider ConnectionStringProvider { get; set; }

        public IStorageCredentialsValidator StorageCredentialsValidator { get; set; }

        public ITypeLocator TypeLocator { get; set; }

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
            else if (serviceType == typeof(IHostIdProvider))
            {
                return new FixedHostIdProvider(Guid.NewGuid().ToString("N"));
            }
            else
            {
                return null;
            }
        }
    }
}
