// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class CollectionAsyncCollector<T> : IAsyncCollector<T>
    {
        private readonly ICollection<T> _collection;

        public CollectionAsyncCollector(ICollection<T> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException("collection");
            }

            _collection = collection;
        }

        public Task AddAsync(T item, CancellationToken cancellationToken)
        {
            _collection.Add(item);
            return Task.FromResult(0);
        }
    }
}
