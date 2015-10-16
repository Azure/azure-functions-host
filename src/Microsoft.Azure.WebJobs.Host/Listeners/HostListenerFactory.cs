// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class HostListenerFactory : IListenerFactory
    {
        private readonly IEnumerable<IFunctionDefinition> _functionDefinitions;
        private readonly SingletonManager _singletonManager;

        public HostListenerFactory(IEnumerable<IFunctionDefinition> functionDefinitions, SingletonManager singletonManager)
        {
            _functionDefinitions = functionDefinitions;
            _singletonManager = singletonManager;
        }

        public async Task<IListener> CreateAsync(CancellationToken cancellationToken)
        {
            List<IListener> listeners = new List<IListener>();

            foreach (IFunctionDefinition functionDefinition in _functionDefinitions)
            {
                IListenerFactory listenerFactory = functionDefinition.ListenerFactory;

                if (listenerFactory == null)
                {
                    continue;
                }

                IListener listener = await listenerFactory.CreateAsync(cancellationToken);

                // if the listener is a Singleton, wrap it with our SingletonListener
                SingletonAttribute singletonAttribute = SingletonManager.GetListenerSingletonOrNull(listener.GetType(), functionDefinition.Descriptor.Method);
                if (singletonAttribute != null)
                {
                    listener = new SingletonListener(functionDefinition.Descriptor.Method, singletonAttribute, _singletonManager, listener);
                }

                listeners.Add(listener);
            }

            return new CompositeListener(listeners);
        }
    }
}
