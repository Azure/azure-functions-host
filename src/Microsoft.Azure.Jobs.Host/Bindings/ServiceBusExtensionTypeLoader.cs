using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs
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
