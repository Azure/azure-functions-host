// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class UserTypeArgumentBindingProvider : IQueueTriggerArgumentBindingProvider
    {
        public ITriggerDataArgumentBinding<BrokeredMessage> TryCreate(ParameterInfo parameter)
        {
            // At indexing time, attempt to bind all types.
            // (Whether or not actual binding is possible depends on the message shape at runtime.)
            return CreateBinding(parameter.ParameterType);
        }

        private static ITriggerDataArgumentBinding<BrokeredMessage> CreateBinding(Type itemType)
        {
            Type genericType = typeof(UserTypeArgumentBinding<>).MakeGenericType(itemType);
            return (ITriggerDataArgumentBinding<BrokeredMessage>)Activator.CreateInstance(genericType);
        }

        private class UserTypeArgumentBinding<TInput> : ITriggerDataArgumentBinding<BrokeredMessage>
        {
            private readonly IBindingDataProvider _bindingDataProvider;

            public UserTypeArgumentBinding()
            {
                _bindingDataProvider = BindingDataProvider.FromType(typeof(TInput));
            }

            public Type ValueType
            {
                get { return typeof(TInput); }
            }

            public IReadOnlyDictionary<string, Type> BindingDataContract
            {
                get { return _bindingDataProvider != null ? _bindingDataProvider.Contract : null; }
            }

            public async Task<ITriggerData> BindAsync(BrokeredMessage value, ValueBindingContext context)
            {
                IValueProvider provider;
                BrokeredMessage clone = value.Clone();

                TInput contents = await GetBody(value, context);

                if (contents == null)
                {
                    provider = await BrokeredMessageValueProvider.CreateAsync(clone, null, ValueType,
                        context.CancellationToken);
                    return new TriggerData(provider, null);
                }

                provider = await BrokeredMessageValueProvider.CreateAsync(clone, contents, ValueType,
                    context.CancellationToken);

                IReadOnlyDictionary<string, object> bindingData = (_bindingDataProvider != null)
                    ? _bindingDataProvider.GetBindingData(contents) : null;

                return new TriggerData(provider, bindingData);
            }

            private static async Task<TInput> GetBody(BrokeredMessage message, ValueBindingContext context)
            {
                if (message.ContentType == ContentTypes.ApplicationJson)
                {
                    string contents;

                    using (Stream stream = message.GetBody<Stream>())
                    {
                        if (stream == null)
                        {
                            return default(TInput);
                        }

                        using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
                        {
                            context.CancellationToken.ThrowIfCancellationRequested();
                            contents = await reader.ReadToEndAsync();
                        }
                    }

                    try
                    {
                        return JsonConvert.DeserializeObject<TInput>(contents, JsonSerialization.Settings);
                    }
                    catch (JsonException e)
                    {
                        // Easy to have the queue payload not deserialize properly. So give a useful error. 
                        string msg = string.Format(
        @"Binding parameters to complex objects (such as '{0}') uses Json.NET serialization. 
1. Bind the parameter type as 'string' instead of '{0}' to get the raw values and avoid JSON deserialization, or
2. Change the queue payload to be valid json. The JSON parser failed: {1}
", typeof(TInput).Name, e.Message);
                        throw new InvalidOperationException(msg);
                    }
                }
                else
                {
                    return message.GetBody<TInput>();
                }
            }
        }
    }
}
