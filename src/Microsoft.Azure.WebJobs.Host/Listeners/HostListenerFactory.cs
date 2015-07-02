// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
                listeners.Add(listener);
            }

            return new CompositeListener(listeners);
        }
    }
}
