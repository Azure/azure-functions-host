// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal class SharedListenerContainer
    {
        private readonly IDictionary<Type, object> _items = new Dictionary<Type, object>();

        public TListener GetOrCreate<TListener>(IFactory<TListener> factory)
        {
            Type listenerType = typeof(TListener);

            if (_items.ContainsKey(listenerType))
            {
                return (TListener)_items[listenerType];
            }
            else
            {
                TListener listener = factory.Create();
                _items.Add(listenerType, listener);
                return listener;
            }
        }
    }
}
