// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    // Bind TMessage --> TUserType. Use IConverterManager for the conversion. 
    // TUserType = the parameter type. 
    internal class CustomTriggerArgumentBinding<TMessage, TTriggerValue, TUserType> : 
        SimpleTriggerArgumentBinding<TMessage, TTriggerValue>
    {
        private readonly FuncConverter<TMessage, Attribute, TUserType> _converter;

        public CustomTriggerArgumentBinding(
            ITriggerBindingStrategy<TMessage, TTriggerValue> bindingStrategy, 
            IConverterManager converterManager,
            FuncConverter<TMessage, Attribute, TUserType> converter) :
            base(bindingStrategy, converterManager)
        {
            if (converter == null)
            {
                throw new ArgumentNullException("converter");
            }
            this._converter = converter;
            this.ElementType = typeof(TUserType);
        }

        internal override object Convert(TMessage value, Dictionary<string, object> bindingData)
        {
            var obj = _converter(value, null, null);
            return obj;
        }
    }
}