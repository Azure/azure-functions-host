// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal class QueueTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private static readonly IQueueTriggerArgumentBindingProvider _innerProvider =
            new CompositeArgumentBindingProvider(
                new ConverterArgumentBindingProvider<CloudQueueMessage>(
                    new StorageQueueMessageToCloudQueueMessageConverter()),
                new ConverterArgumentBindingProvider<string>(new StorageQueueMessageToStringConverter()),
                new ConverterArgumentBindingProvider<byte[]>(new StorageQueueMessageToByteArrayConverter()),
                new UserTypeArgumentBindingProvider()); // Must come last, because it will attempt to bind all types.

        private readonly INameResolver _nameResolver;
        private readonly IStorageAccountProvider _accountProvider;
        private readonly IQueueConfiguration _queueConfiguration;

        public QueueTriggerAttributeBindingProvider(INameResolver nameResolver, IStorageAccountProvider accountProvider,
            IQueueConfiguration queueConfiguration)
        {
            if (accountProvider == null)
            {
                throw new ArgumentNullException("accountProvider");
            }

            if (queueConfiguration == null)
            {
                throw new ArgumentNullException("queueConfiguration");
            }

            _nameResolver = nameResolver;
            _accountProvider = accountProvider;
            _queueConfiguration = queueConfiguration;
        }

        public async Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            QueueTriggerAttribute queueTrigger = parameter.GetCustomAttribute<QueueTriggerAttribute>(inherit: false);

            if (queueTrigger == null)
            {
                return null;
            }

            string queueName = Resolve(queueTrigger.QueueName);
            queueName = NormalizeAndValidate(queueName);

            ITriggerDataArgumentBinding<IStorageQueueMessage> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException(
                    "Can't bind QueueTrigger to type '" + parameter.ParameterType + "'.");
            }

            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(context.CancellationToken);
            IStorageQueueClient client = account.CreateQueueClient();
            IStorageQueue queue = client.GetQueueReference(queueName);

            ITriggerBinding binding = new QueueTriggerBinding(parameter.Name, queue, argumentBinding,
                _queueConfiguration);
            return binding;
        }

        private static string NormalizeAndValidate(string queueName)
        {
            queueName = queueName.ToLowerInvariant(); // must be lowercase. coerce here to be nice.
            QueueClient.ValidateQueueName(queueName);
            return queueName;
        }

        private string Resolve(string queueName)
        {
            if (_nameResolver == null)
            {
                return queueName;
            }

            return _nameResolver.ResolveWholeString(queueName);
        }
    }
}
