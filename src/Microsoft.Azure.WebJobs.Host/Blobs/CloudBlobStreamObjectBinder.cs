// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Blobs.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal static class CloudBlobStreamObjectBinder
    {
        public static IBlobArgumentBindingProvider CreateReadBindingProvider(Type binderType)
        {
            VerifyDefaultConstructor(binderType);
            Type valueType = GetBindingValueType(binderType);
            Type bindingProviderType = typeof(ObjectArgumentBindingProvider<,>).MakeGenericType(valueType, binderType);
            return (IBlobArgumentBindingProvider)Activator.CreateInstance(bindingProviderType);
        }

        public static IBlobArgumentBindingProvider CreateWriteBindingProvider(Type binderType,
            IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter)
        {
            VerifyDefaultConstructor(binderType);
            Type valueType = GetBindingValueType(binderType);
            Type bindingProviderType = typeof(OutObjectArgumentBindingProvider<,>).MakeGenericType(valueType,
                binderType);
            return (IBlobArgumentBindingProvider)Activator.CreateInstance(bindingProviderType,
                blobWrittenWatcherGetter);
        }

        private static Type GetBindingValueType(Type binderType)
        {
            Type binderInterfaceType = GetCloudBlobStreamBinderInterface(binderType);
            Debug.Assert(binderInterfaceType != null);
            return binderInterfaceType.GetGenericArguments()[0];
        }

        private static Type GetCloudBlobStreamBinderInterface(Type binderType)
        {
            return binderType.GetInterfaces().First(
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICloudBlobStreamBinder<>));
        }

        private static void VerifyDefaultConstructor(Type binderType)
        {
            if (!binderType.IsValueType && binderType.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException(
                    "The class implementing ICloudBlobStreamBinder<> must provide a default constructor.");
            }
        }
    }
}
