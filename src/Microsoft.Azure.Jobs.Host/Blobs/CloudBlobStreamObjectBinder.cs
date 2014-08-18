// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal static class CloudBlobStreamObjectBinder
    {
        public static ICloudBlobStreamObjectBinder Create(Type cloudBlobStreamBinderType, out Type valueType)
        {
            Type genericType = GetCloudBlobStreamBinderInterface(cloudBlobStreamBinderType);
            valueType = genericType.GetGenericArguments()[0];
            object innerBinder = Activator.CreateInstance(cloudBlobStreamBinderType);
            Type typelessBinderType = typeof(CloudBlobStreamObjectBinder<>).MakeGenericType(valueType);
            return (ICloudBlobStreamObjectBinder)Activator.CreateInstance(typelessBinderType, innerBinder);
        }

        private static Type GetCloudBlobStreamBinderInterface(Type cloudBlobStreamBinderType)
        {
            return cloudBlobStreamBinderType.GetInterfaces().First(
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICloudBlobStreamBinder<>));
        }
    }
}
