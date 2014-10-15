// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

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

        private readonly IStorageAccountProvider _accountProvider;

        public QueueAttributeBindingProvider(IStorageAccountProvider accountProvider)
        {
            if (accountProvider == null)
            {
                throw new ArgumentNullException("accountProvider");
            }

            _accountProvider = accountProvider;
        }

        public async Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            QueueAttribute queueAttribute = parameter.GetCustomAttribute<QueueAttribute>(inherit: false);

            if (queueAttribute == null)
            {
                return null;
            }

            string queueName = context.Resolve(queueAttribute.QueueName);
            IBindableQueuePath path = BindableQueuePath.Create(queueName);
            path.ValidateContractCompatibility(context.BindingDataContract);

            IArgumentBinding<IStorageQueue> argumentBinding = _innerProvider.TryCreate(parameter);
            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind Queue to type '" + parameter.ParameterType + "'.");
            }

            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(context.CancellationToken);
            IStorageQueueClient client = account.CreateQueueClient();
            IBinding binding = new QueueBinding(parameter.Name, argumentBinding, client, path);
            return binding;
        }
    }
}
