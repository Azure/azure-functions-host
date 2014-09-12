// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class CollectionCollector<T> : ICollector<T>
    {
        private readonly ICollection<T> _collection;

        public CollectionCollector(ICollection<T> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException("collection");
            }

            _collection = collection;
        }

        public void Add(T item)
        {
            _collection.Add(item);
        }
    }
}
