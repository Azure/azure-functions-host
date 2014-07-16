// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host
{
    internal static class ServiceProviderExtensions
    {
        public static IStorageAccountProvider GetStorageAccountProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IStorageAccountProvider>(serviceProvider);
        }

        public static IStorageCredentialsValidator GetStorageCredentialsValidator(this IServiceProvider serviceProvider)
        {
            return GetService<IStorageCredentialsValidator>(serviceProvider);
        }

        public static IConnectionStringProvider GetConnectionStringProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IConnectionStringProvider>(serviceProvider);
        }

        public static ITypeLocator GetTypeLocator(this IServiceProvider serviceProvider)
        {
            return GetService<ITypeLocator>(serviceProvider);
        }

        public static INameResolver GetNameResolver(this IServiceProvider serviceProvider)
        {
            return GetService<INameResolver>(serviceProvider);
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
