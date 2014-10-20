// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal sealed class CompositeListenerFactory : IListenerFactory
    {
        private readonly IEnumerable<IListenerFactory> _listenerFactories;

        public CompositeListenerFactory(params IListenerFactory[] listenerFactories)
        {
            _listenerFactories = listenerFactories;
        }

        public async Task<IListener> CreateAsync(Executors.IFunctionExecutor executor, ListenerFactoryContext context)
        {
            List<IListener> listeners = new List<IListener>();

            foreach (IListenerFactory listenerFactory in _listenerFactories)
            {
                IListener listener = await listenerFactory.CreateAsync(executor, context);
                listeners.Add(listener);
            }

            return new CompositeListener(listeners);
        }
    }
}
