// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    internal class TestJobHostConfiguration : IServiceProvider
    {
        public IFunctionIndexProvider FunctionIndexProvider { get; set; }

        public IQueueConfiguration Queues { get; set; }

        public IServiceBusAccountProvider ServiceBusAccountProvider { get; set; }

        public IStorageAccountProvider StorageAccountProvider { get; set; }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IBackgroundExceptionDispatcher))
            {
                return BackgroundExceptionDispatcher.Instance;
            }
            else if (serviceType == typeof (IConsoleProvider))
            {
                return new NullConsoleProvider();
            }
            else if (serviceType == typeof(IFunctionIndexProvider))
            {
                return FunctionIndexProvider;
            }
            else if (serviceType == typeof(IFunctionInstanceLoggerProvider))
            {
                return new NullFunctionInstanceLoggerProvider();
            }
            else if (serviceType == typeof(IFunctionOutputLoggerProvider))
            {
                return new NullFunctionOutputLoggerProvider();
            }
            else if (serviceType == typeof(IHostIdProvider))
            {
                return new FixedHostIdProvider(Guid.NewGuid().ToString("N"));
            }
            else if (serviceType == typeof(IHostInstanceLoggerProvider))
            {
                return new NullHostInstanceLoggerProvider();
            }
            else if (serviceType == typeof(IQueueConfiguration))
            {
                return Queues;
            }
            else if (serviceType == typeof(IServiceBusAccountProvider))
            {
                return ServiceBusAccountProvider;
            }
            else if (serviceType == typeof(IStorageAccountProvider))
            {
                return StorageAccountProvider;
            }
            else
            {
                return null;
            }
        }
    }
}
