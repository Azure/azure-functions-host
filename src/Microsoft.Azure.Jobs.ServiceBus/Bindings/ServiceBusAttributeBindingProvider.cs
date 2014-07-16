// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class ServiceBusAttributeBindingProvider : IBindingProvider
    {
        private static readonly IQueueArgumentBindingProvider _innerProvider =
            new CompositeArgumentBindingProvider(
                new BrokeredMessageArgumentBindingProvider(),
                new StringArgumentBindingProvider(),
                new ByteArrayArgumentBindingProvider(),
                new CollectionArgumentBindingProvider(),
                new UserTypeArgumentBindingProvider()); // Must be after collection provider (IEnumerable checks).

        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            ServiceBusAttribute serviceBusAttribute = parameter.GetCustomAttribute<ServiceBusAttribute>(inherit: false);

            if (serviceBusAttribute == null)
            {
                return null;
            }

            string queueOrTopicName = context.Resolve(serviceBusAttribute.QueueOrTopicName);

            IArgumentBinding<ServiceBusEntity> argumentBinding = _innerProvider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind ServiceBus to type '" + parameter.ParameterType + "'.");
            }

            ServiceBusAccount account = ServiceBusAccount.CreateFromConnectionString(
                context.ServiceBusConnectionString);

            return new ServiceBusBinding(parameter.Name, argumentBinding, account, queueOrTopicName);
        }
    }
}
