// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class CollectorValueBinder<TItem> : IOrderedValueBinder
    {
        private readonly CloudQueue _queue;
        private readonly object _value;
        private readonly Type _valueType;
        private readonly ICollection<TItem> _collection;
        private readonly IValueBinder _itemBinder;

        public CollectorValueBinder(CloudQueue queue, object value, Type valueType, ICollection<TItem> collection,
            IValueBinder itemBinder)
        {
            if (value != null && !valueType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException("value is not of the correct type.");
            }

            _queue = queue;
            _value = value;
            _valueType = valueType;
            _collection = collection;
            _itemBinder = itemBinder;
        }

        public int StepOrder
        {
            get { return BindStepOrders.Enqueue; }
        }

        public Type Type
        {
            get { return _valueType; }
        }

        public object GetValue()
        {
            return _value;
        }

        public string ToInvokeString()
        {
            return _queue.Name;
        }

        public async Task SetValueAsync(object value, CancellationToken cancellationToken)
        {
            Debug.Assert(value == null || value == GetValue(),
                "The value argument should be either the same instance as returned by GetValue() or null");

            // Not ByRef, so can ignore value argument.
            foreach (TItem item in _collection)
            {
                await _itemBinder.SetValueAsync(item, cancellationToken);
            }
        }
    }
}
