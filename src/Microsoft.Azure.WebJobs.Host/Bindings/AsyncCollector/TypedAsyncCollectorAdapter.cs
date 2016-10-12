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
        private readonly FuncConverter<TSrc, TAttribute, TDest> _convert;
        private readonly TAttribute _attrResolved;
        private readonly ValueBindingContext _context;

        public TypedAsyncCollectorAdapter(
            IAsyncCollector<TDest> inner,
            FuncConverter<TSrc, TAttribute, TDest> convert, 
            TAttribute attrResolved,
            ValueBindingContext context)
        {
            if (convert == null)
            {
                throw new ArgumentNullException("convert");
            }

            _inner = inner;
            _convert = convert;
            _attrResolved = attrResolved;
            _context = context;
        }

        public Task AddAsync(TSrc item, CancellationToken cancellationToken = default(CancellationToken))
        {
            TDest x = _convert(item, _attrResolved, _context);
            return _inner.AddAsync(x, cancellationToken);
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return _inner.FlushAsync(cancellationToken);
        }        
    }
}