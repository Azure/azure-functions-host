// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
