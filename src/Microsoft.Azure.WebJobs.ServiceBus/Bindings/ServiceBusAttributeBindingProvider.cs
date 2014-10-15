// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class ServiceBusAttributeBindingProvider : IBindingProvider
    {
        private static readonly IQueueArgumentBindingProvider _innerProvider =
            new CompositeArgumentBindingProvider(
                new BrokeredMessageArgumentBindingProvider(),
                new StringArgumentBindingProvider(),
                new ByteArrayArgumentBindingProvider(),
                new UserTypeArgumentBindingProvider(),
                new CollectorArgumentBindingProvider(),
                new AsyncCollectorArgumentBindingProvider());

        private readonly IServiceBusAccountProvider _accountProvider;

        public ServiceBusAttributeBindingProvider(IServiceBusAccountProvider accountProvider)
        {
            if (accountProvider == null)
            {
                throw new ArgumentNullException("accountProvider");
            }

            _accountProvider = accountProvider;
        }

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            ServiceBusAttribute serviceBusAttribute = parameter.GetCustomAttribute<ServiceBusAttribute>(inherit: false);

            if (serviceBusAttribute == null)
            {
                return Task.FromResult<IBinding>(null);
            }

            string queueOrTopicName = context.Resolve(serviceBusAttribute.QueueOrTopicName);
            IBindableServiceBusPath path = BindableServiceBusPath.Create(queueOrTopicName);
            path.ValidateContractCompatibility(context.BindingDataContract);

            IArgumentBinding<ServiceBusEntity> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind ServiceBus to type '" + parameter.ParameterType + "'.");
            }

            string connectionString = _accountProvider.GetConnectionString();
            ServiceBusAccount account = ServiceBusAccount.CreateFromConnectionString(connectionString);

            IBinding binding = new ServiceBusBinding(parameter.Name, argumentBinding, account, path);
            return Task.FromResult(binding);
        }
    }
}
