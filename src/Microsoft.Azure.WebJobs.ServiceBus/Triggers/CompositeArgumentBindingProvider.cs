// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.ServiceBus;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class CompositeArgumentBindingProvider : IQueueTriggerArgumentBindingProvider
    {
        private readonly IEnumerable<IQueueTriggerArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(params IQueueTriggerArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public ITriggerDataArgumentBinding<Message> TryCreate(ParameterInfo parameter)
        {
            foreach (IQueueTriggerArgumentBindingProvider provider in _providers)
            {
                ITriggerDataArgumentBinding<Message> binding = provider.TryCreate(parameter);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
