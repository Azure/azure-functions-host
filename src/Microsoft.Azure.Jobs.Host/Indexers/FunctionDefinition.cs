// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal class FunctionDefinition : IFunctionDefinition
    {
        private readonly IFunctionInstanceFactory _instanceFactory;
        private readonly IListenerFactory _listenerFactory;

        public FunctionDefinition(IFunctionInstanceFactory instanceFactory, IListenerFactory listenerFactory)
        {
            _instanceFactory = instanceFactory;
            _listenerFactory = listenerFactory;
        }

        public IFunctionInstanceFactory InstanceFactory
        {
            get { return _instanceFactory; }
        }

        public IListenerFactory ListenerFactory
        {
            get { return _listenerFactory; }
        }
    }
}
