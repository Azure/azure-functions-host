// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    internal static class ServiceBusIndexer
    {
        public static bool HasSdkAttribute (MethodInfo method)
        {
            return method.GetParameters().Any(p => p.GetCustomAttributesData().Any(a => a.AttributeType.Assembly == typeof(ServiceBusIndexer).Assembly));
        }
    }
}
