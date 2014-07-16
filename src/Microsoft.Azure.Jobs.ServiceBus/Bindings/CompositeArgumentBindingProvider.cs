// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class CompositeArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        private readonly IEnumerable<IQueueArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(params IQueueArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<ServiceBusEntity> TryCreate(ParameterInfo parameter)
        {
            foreach (IQueueArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<ServiceBusEntity> binding = provider.TryCreate(parameter);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
