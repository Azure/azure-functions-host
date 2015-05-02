// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class NullListenerFactory : IListenerFactory
    {
        public Task<IListener> CreateAsync(ListenerFactoryContext context)
        {
            IListener listener = new NullListener();
            return Task.FromResult(listener);
        }
    }
}
