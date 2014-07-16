// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal static class ServiceBusExtensionTypeLoader
    {
        private static readonly Assembly serviceBusExtensionAssembly;
        static ServiceBusExtensionTypeLoader()
        {
            try
            {
                serviceBusExtensionAssembly = Assembly.Load("Microsoft.Azure.Jobs.ServiceBus");
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
