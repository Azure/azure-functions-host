// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class CloudQueueArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(CloudQueue))
            {
                return null;
            }

            return new CloudQueueArgumentBinding();
        }

        private class CloudQueueArgumentBinding : IArgumentBinding<CloudQueue>
        {
            public Type ValueType
            {
                get { return typeof(CloudQueue); }
            }

            public IValueProvider Bind(CloudQueue value, FunctionBindingContext context)
            {
                value.CreateIfNotExists();
                return new QueueValueProvider(value, value, typeof(CloudQueue));
            }
        }
    }
}
