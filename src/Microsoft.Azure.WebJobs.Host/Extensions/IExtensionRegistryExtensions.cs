// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Provides extension methods for <see cref="IExtensionRegistry"/>./>
    /// </summary>
    public static class IExtensionRegistryExtensions
    {
        /// <summary>
        /// Registers the specified instance. 
        /// </summary>
        /// <typeparam name="TExtension">The service type to register the instance for.</typeparam>
        /// <param name="registry">The registry instance.</param>
        /// <param name="extension">The instance to register.</param>
        public static void RegisterExtension<TExtension>(this IExtensionRegistry registry, TExtension extension)
        {
            if (registry == null)
            {
                throw new ArgumentNullException("registry");
            }

            registry.RegisterExtension(typeof(TExtension), extension);
        }

        /// <summary>
        /// Returns the collection of extension instances registered for the specified type.
        /// </summary>
        /// <typeparam name="TExtension">The service type to get extensions for.</typeparam>
        /// <param name="registry">The registry instance.</param>
        /// <returns>The collection of extension instances.</returns>
        public static IEnumerable<TExtension> GetExtensions<TExtension>(this IExtensionRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException("registry");
            }

            return registry.GetExtensions(typeof(TExtension)).Cast<TExtension>();
        }
    }
}
