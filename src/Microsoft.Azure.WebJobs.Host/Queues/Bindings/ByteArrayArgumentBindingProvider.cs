// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class ByteArrayArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut || parameter.ParameterType != typeof(byte[]).MakeByRefType())
            {
                return null;
            }

            return new ByteArrayArgumentBinding();
        }

        private class ByteArrayArgumentBinding : IArgumentBinding<CloudQueue>
        {
            public Type ValueType
            {
                get { return typeof(byte[]); }
            }

            /// <remarks>
            /// As this method handles out byte array parameter it distinguishes following possible scenarios:
            /// <item>
            /// <description>
            /// If the value is <see langword="null"/>, no message will be sent.
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// If the value is an empty byte array, a message with empty content will be sent.
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// If the value is a non-empty byte array, a message with content from given argument will be sent.
            /// </description>
            /// </item>
            /// </remarks>
            public Task<IValueProvider> BindAsync(CloudQueue value, ValueBindingContext context)
            {
                IValueProvider provider = new NonNullConverterValueBinder<byte[]>(value,
                    new ByteArrayToCloudQueueMessageConverter(), context.MessageEnqueuedWatcher);
                return Task.FromResult(provider);
            }
        }
    }
}
