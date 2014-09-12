// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class QueueAttributeBindingProvider : IBindingProvider
    {
        private static readonly IQueueArgumentBindingProvider _innerProvider = new CompositeArgumentBindingProvider(
            new CloudQueueArgumentBindingProvider(),
            new CloudQueueMessageArgumentBindingProvider(),
            new StringArgumentBindingProvider(),
            new ByteArrayArgumentBindingProvider(),
            new UserTypeArgumentBindingProvider(),
            new CollectorArgumentBindingProvider(),
            new AsyncCollectorArgumentBindingProvider());

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            QueueAttribute queueAttribute = parameter.GetCustomAttribute<QueueAttribute>(inherit: false);

            if (queueAttribute == null)
            {
                return Task.FromResult<IBinding>(null);
            }

            string queueName = context.Resolve(queueAttribute.QueueName);
            IBindableQueuePath path = BindableQueuePath.Create(queueName);
            path.ValidateContractCompatibility(context.BindingDataContract);

            IArgumentBinding<CloudQueue> argumentBinding = _innerProvider.TryCreate(parameter);
            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind Queue to type '" + parameter.ParameterType + "'.");
            }

            IBinding binding = new QueueBinding(parameter.Name, argumentBinding,
                context.StorageAccount.CreateCloudQueueClient(), path);
            return Task.FromResult(binding);
        }
    }
}
