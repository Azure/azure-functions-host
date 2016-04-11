// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // IAsyncCollector<TSrc> --> IAsyncCollector<TDest>
    internal class TypedAsyncCollectorAdapter<TSrc, TDest, TAttribute> : IAsyncCollector<TSrc>
        where TAttribute : Attribute
    {
        private readonly IAsyncCollector<TDest> _inner;
        private readonly Func<TSrc, TAttribute, TDest> _convert;
        private readonly TAttribute _attrResolved;

        public TypedAsyncCollectorAdapter(
            IAsyncCollector<TDest> inner, 
            Func<TSrc, TAttribute, TDest> convert, 
            TAttribute attrResolved)
        {
            if (convert == null)
            {
                throw new ArgumentNullException("convert");
            }

            _inner = inner;
            _convert = convert;
            _attrResolved = attrResolved;
        }

        public Task AddAsync(TSrc item, CancellationToken cancellationToken = default(CancellationToken))
        {
            TDest x = _convert(item, _attrResolved);
            return _inner.AddAsync(x, cancellationToken);
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return _inner.FlushAsync(cancellationToken);
        }        
    }
}