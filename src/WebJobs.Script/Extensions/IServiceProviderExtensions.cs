// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class IServiceProviderExtensions
    {
        /// <summary>
        /// Gets the specified ScriptHost level service, or null.
        /// </summary>
        /// <typeparam name="TService">The service to query for.</typeparam>
        /// <param name="serviceProvider">The <see cref="IServiceProvider"/> to query.</param>
        /// <returns>The service or null.</returns>
        public static TService GetScriptHostServiceOrNull<TService>(this IServiceProvider serviceProvider) where TService : class
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            try
            {
                var hostManager = serviceProvider.GetService<IScriptHostManager>();
                if (Utility.TryGetHostService(hostManager, out TService service))
                {
                    return service;
                }
            }
            catch
            {
                // can get exceptions if the host is being disposed
            }

            return null;
        }
    }
}
