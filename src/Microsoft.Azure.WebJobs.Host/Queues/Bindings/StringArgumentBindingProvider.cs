// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class StringArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<IStorageQueue> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut || parameter.ParameterType != typeof(string).MakeByRefType())
            {
                return null;
            }

            return new StringArgumentBinding();
        }

        private class StringArgumentBinding : IArgumentBinding<IStorageQueue>
        {
            public Type ValueType
            {
                get { return typeof(string); }
            }

            /// <remarks>
            /// The out string parameter is processed as follows:
            /// <list type="bullet">
            /// <item>
            /// <description>
            /// If the value is <see langword="null"/>, no message will be sent.
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// If the value is an empty string, a message with empty content will be sent.
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// If the value is a non-empty string, a message with that content will be sent.
            /// </description>
            /// </item>
            /// </list>
            /// </remarks>
            public Task<IValueProvider> BindAsync(IStorageQueue value, ValueBindingContext context)
            {
                IValueProvider provider = new NonNullConverterValueBinder<string>(value,
                    new StringToStorageQueueMessageConverter(value), context.MessageEnqueuedWatcher);
                return Task.FromResult(provider);
            }
        }
    }
}
