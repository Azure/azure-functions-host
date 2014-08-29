// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class PocoEntityCollectorBinder<T> : IValueBinder, IWatchable
    {
        private readonly CloudTable _table;
        private readonly PocoEntityWriter<T> _value;
        private readonly Type _valueType;

        public PocoEntityCollectorBinder(CloudTable table, PocoEntityWriter<T> value, Type valueType)
        {
            if (value != null && !valueType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException("value is not of the correct type.");
            }

            _table = table;
            _value = value;
            _valueType = valueType;
        }

        public Type Type
        {
            get { return _valueType; }
        }

        public IWatcher Watcher
        {
            get
            {
                return _value;
            }
        }

        public object GetValue()
        {
            return _value;
        }

        public string ToInvokeString()
        {
            return _table.Name;
        }

        public Task SetValueAsync(object value, CancellationToken cancellationToken)
        {
            return _value.FlushAsync(cancellationToken);
        }

        public ParameterLog GetStatus()
        {
            return _value.GetStatus();
        }
    }
}
