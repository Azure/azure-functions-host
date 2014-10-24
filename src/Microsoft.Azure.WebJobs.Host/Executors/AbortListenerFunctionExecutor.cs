// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Listeners;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class AbortListenerFunctionExecutor : IFunctionExecutor
    {
        private readonly IListenerFactory _abortListenerFactory;
        private readonly IFunctionExecutor _abortExecutor;
        private readonly IFunctionExecutor _innerExecutor;

        public AbortListenerFunctionExecutor(IListenerFactory abortListenerFactory, IFunctionExecutor abortExecutor,
            IFunctionExecutor innerExecutor)
        {
            _abortListenerFactory = abortListenerFactory;
            _abortExecutor = abortExecutor;
            _innerExecutor = innerExecutor;
        }

        public async Task<IDelayedException> TryExecuteAsync(IFunctionInstance instance,
            CancellationToken cancellationToken)
        {
            IDelayedException result;

            using (IListener listener = await CreateListenerAsync(cancellationToken))
            {
                await listener.StartAsync(cancellationToken);

                result = await _innerExecutor.TryExecuteAsync(instance, cancellationToken);

                await listener.StopAsync(cancellationToken);
            }

            return result;
        }

        private Task<IListener> CreateListenerAsync(CancellationToken cancellationToken)
        {
            return _abortListenerFactory.CreateAsync(_abortExecutor, cancellationToken);
        }
    }
}
