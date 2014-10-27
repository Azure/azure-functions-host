// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeServiceProvider : IServiceProvider
    {
        public IBackgroundExceptionDispatcher BackgroundExceptionDispatcher { get; set; }

        public IBindingProvider BindingProvider { get; set; }

        public IConsoleProvider ConsoleProvider { get; set; }

        public IFunctionIndexProvider FunctionIndexProvider { get; set; }

        public IFunctionInstanceLoggerProvider FunctionInstanceLoggerProvider { get; set; }

        public IFunctionOutputLoggerProvider FunctionOutputLoggerProvider { get; set; }

        public IHostIdProvider HostIdProvider { get; set; }

        public IHostInstanceLoggerProvider HostInstanceLoggerProvider { get; set; }

        public IQueueConfiguration QueueConfiguration { get; set; }

        public IServiceBusAccountProvider ServiceBusAccountProvider { get; set; }

        public IStorageAccountProvider StorageAccountProvider { get; set; }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IBackgroundExceptionDispatcher))
            {
                return BackgroundExceptionDispatcher;
            }
            else if (serviceType == typeof(IBindingProvider))
            {
                return BindingProvider;
            }
            else if (serviceType == typeof(IConsoleProvider))
            {
                return ConsoleProvider;
            }
            else if (serviceType == typeof(IFunctionIndexProvider))
            {
                return FunctionIndexProvider;
            }
            else if (serviceType == typeof(IFunctionInstanceLoggerProvider))
            {
                return FunctionInstanceLoggerProvider;
            }
            else if (serviceType == typeof(IFunctionOutputLoggerProvider))
            {
                return FunctionOutputLoggerProvider;
            }
            else if (serviceType == typeof(IHostIdProvider))
            {
                return HostIdProvider;
            }
            else if (serviceType == typeof(IHostInstanceLoggerProvider))
            {
                return HostInstanceLoggerProvider;
            }
            else if (serviceType == typeof(IQueueConfiguration))
            {
                return QueueConfiguration;
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
