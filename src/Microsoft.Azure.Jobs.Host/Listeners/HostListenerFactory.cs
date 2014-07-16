// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;

namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal class HostListenerFactory : IListenerFactory
    {
        private readonly IEnumerable<IFunctionDefinition> _functionDefinitions;
        private readonly IListenerFactory _sharedQueueListenerFactory;
        private readonly IListenerFactory _instanceQueueListenerFactory;

        public HostListenerFactory(IEnumerable<IFunctionDefinition> functionDefinitions,
            IListenerFactory sharedQueueListenerFactory,
            IListenerFactory instanceQueueListenerFactory)
        {
            _functionDefinitions = functionDefinitions;
            _sharedQueueListenerFactory = sharedQueueListenerFactory;
            _instanceQueueListenerFactory = instanceQueueListenerFactory;
        }

        public IListener Create(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            List<IListener> listeners = new List<IListener>();

            foreach (IFunctionDefinition functionDefinition in _functionDefinitions)
            {
                IListenerFactory listenerFactory = functionDefinition.ListenerFactory;

                if (listenerFactory == null)
                {
                    continue;
                }

                IListener listener = listenerFactory.Create(executor, context);
                listeners.Add(listener);
            }

            IListener sharedQueueListener = _sharedQueueListenerFactory.Create(executor, context);

            if (sharedQueueListener != null)
            {
                listeners.Add(sharedQueueListener);
            }

            IListener instanceQueueListener = _instanceQueueListenerFactory.Create(executor, context);

            if (instanceQueueListener != null)
            {
                listeners.Add(instanceQueueListener);
            }

            return new CompositeListener(listeners);
        }
    }
}
