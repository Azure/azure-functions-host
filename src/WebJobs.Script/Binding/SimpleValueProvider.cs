// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class SimpleValueProvider : IValueProvider
    {
        private readonly Type _type;
        private readonly object _value;
        private readonly string _invokeString;

        public SimpleValueProvider(Type type, object value, string invokeString)
        {
            _type = type;
            _value = value;
            _invokeString = invokeString;
        }

        public Type Type
        {
            get
            {
                return _type;
            }
        }

        public Task<object> GetValueAsync()
        {
            return Task.FromResult(_value);
        }

        public string ToInvokeString()
        {
            return _invokeString;
        }
    }
}
