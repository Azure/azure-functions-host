// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal static class ServiceBusExtensionTypeLoader
    {
        private static readonly Assembly serviceBusExtensionAssembly;
        static ServiceBusExtensionTypeLoader()
        {
            try
            {
                serviceBusExtensionAssembly = Assembly.Load("Microsoft.Azure.WebJobs.ServiceBus");
            }
            catch
            {
                serviceBusExtensionAssembly = null;
            }
        }

        internal static Type Get(string name)
        {
            if (serviceBusExtensionAssembly == null)
            {
                return null;
            }
            return serviceBusExtensionAssembly.GetType(name, false);
        }
    }
}
