// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class NullListenerFactory : IListenerFactory
    {
        public Task<IListener> CreateAsync(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            IListener listener = new NullListener();
            return Task.FromResult(listener);
        }
    }
}
