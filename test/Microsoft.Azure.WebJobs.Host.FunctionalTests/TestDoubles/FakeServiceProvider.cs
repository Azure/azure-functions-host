// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeServiceProvider : IServiceProvider
    {
        public IBackgroundExceptionDispatcher BackgroundExceptionDispatcher { get; set; }

        public IConnectionStringProvider ConnectionStringProvider { get; set; }

        public IFunctionInstanceLogger FunctionInstanceLogger { get; set; }

        public IHostIdProvider HostIdProvider { get; set; }

        public IHostInstanceLogger HostInstanceLogger { get; set; }

        public IQueueConfiguration QueueConfiguration { get; set; }

        public IStorageAccountProvider StorageAccountProvider { get; set; }

        public IStorageCredentialsValidator StorageCredentialsValidator { get; set; }

        public ITypeLocator TypeLocator { get; set; }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IBackgroundExceptionDispatcher))
            {
                return BackgroundExceptionDispatcher;
            }
            else if (serviceType == typeof(IConnectionStringProvider))
            {
                return ConnectionStringProvider;
            }
            else if (serviceType == typeof(IFunctionInstanceLogger))
            {
                return FunctionInstanceLogger;
            }
            else if (serviceType == typeof(IHostIdProvider))
            {
                return HostIdProvider;
            }
            else if (serviceType == typeof(IHostInstanceLogger))
            {
                return HostInstanceLogger;
            }
            else if (serviceType == typeof(IQueueConfiguration))
            {
                return QueueConfiguration;
            }
            else if (serviceType == typeof(IStorageAccountProvider))
            {
                return StorageAccountProvider;
            }
            else if (serviceType == typeof(IStorageCredentialsValidator))
            {
                return StorageCredentialsValidator;
            }
            else if (serviceType == typeof(ITypeLocator))
            {
                return TypeLocator;
            }
            else
            {
                return null;
            }
        }
    }
}
