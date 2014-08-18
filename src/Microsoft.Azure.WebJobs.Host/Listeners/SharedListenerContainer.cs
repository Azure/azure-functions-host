// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class SharedListenerContainer
    {
        private readonly IDictionary<Type, object> _items = new Dictionary<Type, object>();

        public T GetOrCreate<T>(IFactory<T> factory)
        {
            Type factoryItemType = typeof(T);

            if (_items.ContainsKey(factoryItemType))
            {
                return (T)_items[factoryItemType];
            }
            else
            {
                T listener = factory.Create();
                _items.Add(factoryItemType, listener);
                return listener;
            }
        }
    }
}
