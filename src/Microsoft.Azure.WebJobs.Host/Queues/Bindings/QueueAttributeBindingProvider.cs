// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
        private readonly INameResolver _nameResolver;
        private readonly IStorageAccountProvider _accountProvider;
        private readonly IQueueArgumentBindingProvider _innerProvider;

        public QueueAttributeBindingProvider(INameResolver nameResolver, IStorageAccountProvider accountProvider,
            IContextGetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherGetter)
        {
            if (accountProvider == null)
            {
                throw new ArgumentNullException("accountProvider");
            }

            if (messageEnqueuedWatcherGetter == null)
            {
                throw new ArgumentNullException("messageEnqueuedWatcherGetter");
            }

            _nameResolver = nameResolver;
            _accountProvider = accountProvider;
            _innerProvider = CreateInnerProvider(messageEnqueuedWatcherGetter);
        }
        
        private static IQueueArgumentBindingProvider CreateInnerProvider(
            IContextGetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherGetter)
        {
            return new CompositeArgumentBindingProvider(
                new StorageQueueArgumentBindingProvider(),
                new CloudQueueArgumentBindingProvider(),
                new CloudQueueMessageArgumentBindingProvider(messageEnqueuedWatcherGetter),
                new StringArgumentBindingProvider(messageEnqueuedWatcherGetter),
                new ByteArrayArgumentBindingProvider(messageEnqueuedWatcherGetter),
                new UserTypeArgumentBindingProvider(messageEnqueuedWatcherGetter),
                new CollectorArgumentBindingProvider(),
                new AsyncCollectorArgumentBindingProvider());
        }

        public async Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            QueueAttribute queueAttribute = parameter.GetCustomAttribute<QueueAttribute>(inherit: false);

            if (queueAttribute == null)
            {
                return null;
            }

            string queueName = Resolve(queueAttribute.QueueName);
            IBindableQueuePath path = BindableQueuePath.Create(queueName);
            path.ValidateContractCompatibility(context.BindingDataContract);

            IArgumentBinding<IStorageQueue> argumentBinding = _innerProvider.TryCreate(parameter);
            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind Queue to type '" + parameter.ParameterType + "'.");
            }

            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(context.CancellationToken);
            StorageClientFactoryContext clientFactoryContext = new StorageClientFactoryContext
            {
                Parameter = parameter
            };
            IStorageQueueClient client = account.CreateQueueClient(clientFactoryContext);
            IBinding binding = new QueueBinding(parameter.Name, argumentBinding, client, path);
            return binding;
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
