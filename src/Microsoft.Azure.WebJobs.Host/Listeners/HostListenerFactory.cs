// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class HostListenerFactory : IListenerFactory
    {
        private readonly IEnumerable<IFunctionDefinition> _functionDefinitions;

        public HostListenerFactory(IEnumerable<IFunctionDefinition> functionDefinitions)
        {
            _functionDefinitions = functionDefinitions;
        }

        public async Task<IListener> CreateAsync(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            List<IListener> listeners = new List<IListener>();

            foreach (IFunctionDefinition functionDefinition in _functionDefinitions)
            {
                IListenerFactory listenerFactory = functionDefinition.ListenerFactory;

                if (listenerFactory == null)
                {
                    continue;
                }

                IListener listener = await listenerFactory.CreateAsync(executor, context);
                listeners.Add(listener);
            }

            return new CompositeListener(listeners);
        }
    }
}
