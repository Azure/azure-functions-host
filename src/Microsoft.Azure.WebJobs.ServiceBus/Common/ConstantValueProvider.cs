// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using System;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Describe a constant value, commonly used for inputs. 
    internal class ConstantValueProvider : IValueProvider
    {
        private object _value;
        private string _invokeString;

        public ConstantValueProvider(object value, Type type, string invokeString)
        {
            this._value = value;
            this.Type = type;
            this._invokeString = invokeString;
        }

        public Type Type { get; set; }

        public object GetValue()
        {
            return _value;
        }

        public string ToInvokeString()
        {
            return _invokeString;
        }
    }
}