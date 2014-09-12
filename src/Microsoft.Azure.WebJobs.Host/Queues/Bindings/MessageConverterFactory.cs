// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal static class MessageConverterFactory
    {
        internal static IMessageConverterFactory<TInput> Create<TInput>()
        {
            if (typeof(TInput) == typeof(CloudQueueMessage))
            {
                return (IMessageConverterFactory<TInput>)new CloudQueueMessageArgumentBinding();
            }
            else if (typeof(TInput) == typeof(string))
            {
                return (IMessageConverterFactory<TInput>)new StringMessageConverterFactory();
            }
            else if (typeof(TInput) == typeof(byte[]))
            {
                return (IMessageConverterFactory<TInput>)new ByteArrayMessageConverterFactory();
            }
            else
            {
                if (typeof(IEnumerable).IsAssignableFrom(typeof(TInput)))
                {
                    throw new InvalidOperationException("Nested collections are not supported.");
                }

                return new UserTypeMessageConverterFactory<TInput>();
            }
        }
    }
}
