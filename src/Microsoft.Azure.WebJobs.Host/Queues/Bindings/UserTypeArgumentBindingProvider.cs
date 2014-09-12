// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class UserTypeArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut)
            {
                return null;
            }

            Type parameterType = parameter.ParameterType.GetElementType();

            if (typeof(IEnumerable).IsAssignableFrom(parameterType))
            {
                throw new InvalidOperationException(
                    "Enumerable types are not supported. Use ICollector<T> or IAsyncCollector<T> instead.");
            }
            else if (typeof(object) == parameterType)
            {
                throw new InvalidOperationException("Object element types are not supported.");
            }

            return new UserTypeArgumentBinding(parameterType);
        }
    }
}
