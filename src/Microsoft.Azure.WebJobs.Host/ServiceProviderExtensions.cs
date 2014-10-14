// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host
{
    internal static class ServiceProviderExtensions
    {
        public static IBackgroundExceptionDispatcher GetBackgroundExceptionDispatcher(
            this IServiceProvider serviceProvider)
        {
            return GetService<IBackgroundExceptionDispatcher>(serviceProvider);
        }

        public static IBindingProvider GetBindingProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IBindingProvider>(serviceProvider);
        }

        public static IConnectionStringProvider GetConnectionStringProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IConnectionStringProvider>(serviceProvider);
        }

        public static IFunctionIndexProvider GetFunctionIndexProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IFunctionIndexProvider>(serviceProvider);
        }

        public static IFunctionInstanceLoggerProvider GetFunctionInstanceLoggerProvider(
            this IServiceProvider serviceProvider)
        {
            return GetService<IFunctionInstanceLoggerProvider>(serviceProvider);
        }

        public static IHostIdProvider GetHostIdProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IHostIdProvider>(serviceProvider);
        }

        public static IHostInstanceLoggerProvider GetHostInstanceLoggerProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IHostInstanceLoggerProvider>(serviceProvider);
        }

        public static INameResolver GetNameResolver(this IServiceProvider serviceProvider)
        {
            return GetService<INameResolver>(serviceProvider);
        }

        public static IQueueConfiguration GetQueueConfiguration(this IServiceProvider serviceProvider)
        {
            return GetService<IQueueConfiguration>(serviceProvider);
        }

        public static IServiceBusAccountProvider GetServiceBusAccountProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IServiceBusAccountProvider>(serviceProvider);
        }

        public static IStorageAccountProvider GetStorageAccountProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IStorageAccountProvider>(serviceProvider);
        }

        private static T GetService<T>(this IServiceProvider serviceProvider) where T : class
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException("serviceProvider");
            }

            object service = serviceProvider.GetService(typeof(T));

            if (service == null)
            {
                return null;
            }

            T typedService = service as T;

            if (typedService == null)
            {
                string message = String.Format(CultureInfo.InvariantCulture,
                    "ServiceProvider.GetService({0}) must return an object assignable to {0}.", typeof(T).Name);
                throw new InvalidOperationException(message);
            }

            return typedService;
        }
    }
}
