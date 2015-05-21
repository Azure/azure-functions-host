// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Host
{
    internal static class ServiceProviderExtensions
    {
        public static IJobHostContextFactory GetJobHostContextFactory(this IServiceProvider serviceProvider)
        {
            return GetService<IJobHostContextFactory>(serviceProvider);
        }

        public static IExtensionRegistry GetExtensions(this IServiceProvider serviceProvider)
        {
            return GetService<IExtensionRegistry>(serviceProvider);
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
                     "The returned service must be assignable to {0}.", typeof(T).Name);
                throw new InvalidOperationException(message);
            }

            return typedService;
        }
    }
}
