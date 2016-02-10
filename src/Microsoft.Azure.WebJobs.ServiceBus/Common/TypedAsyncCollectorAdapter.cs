// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // IAsyncCollector<TSrc> --> IAsyncCollector<TDest>
    internal class TypedAsyncCollectorAdapter<TSrc, TDest> : IFlushCollector<TSrc>
    {
        private readonly IFlushCollector<TDest> _inner;
        private readonly Func<TSrc, TDest> _convert;

        public Task AddAsync(TSrc item, CancellationToken cancellationToken = default(CancellationToken))
        {
            TDest x = _convert(item);
            return _inner.AddAsync(x, cancellationToken);
        }

        public Task FlushAsync()
        {
            return _inner.FlushAsync();
        }

        public TypedAsyncCollectorAdapter(IFlushCollector<TDest> inner, Func<TSrc, TDest> convert)
        {
            _inner = inner;
            _convert = convert;
        }
    }
}