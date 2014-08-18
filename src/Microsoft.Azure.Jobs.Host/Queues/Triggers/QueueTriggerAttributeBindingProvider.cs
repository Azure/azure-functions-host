// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal class QueueTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private static readonly IQueueTriggerArgumentBindingProvider _innerProvider =
            new CompositeArgumentBindingProvider(
                new ConverterArgumentBindingProvider<CloudQueueMessage>(new IdentityConverter<CloudQueueMessage>()),
                new ConverterArgumentBindingProvider<string>(new CloudQueueMessageToStringConverter()),
                new ConverterArgumentBindingProvider<byte[]>(new CloudQueueMessageToByteArrayConverter()),
                new UserTypeArgumentBindingProvider()); // Must come last, because it will attempt to bind all types.

        public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            QueueTriggerAttribute queueTrigger = parameter.GetCustomAttribute<QueueTriggerAttribute>(inherit: false);

            if (queueTrigger == null)
            {
                return Task.FromResult<ITriggerBinding>(null);
            }

            string queueName = context.Resolve(queueTrigger.QueueName);
            queueName = NormalizeAndValidate(queueName);

            ITriggerDataArgumentBinding<CloudQueueMessage> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind QueueTrigger to type '" + parameter.ParameterType + "'.");
            }

            ITriggerBinding binding = new QueueTriggerBinding(parameter.Name, argumentBinding, context.StorageAccount,
                queueName);
            return Task.FromResult(binding);
        }

        private static string NormalizeAndValidate(string queueName)
        {
            queueName = queueName.ToLowerInvariant(); // must be lowercase. coerce here to be nice.
            QueueClient.ValidateQueueName(queueName);
            return queueName;
        }
    }
}
