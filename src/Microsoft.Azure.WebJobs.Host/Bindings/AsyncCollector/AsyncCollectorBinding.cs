// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Common binding to bind an AsyncCollector-compatible value to a user parameter. 
    // This binding is static per parameter, and then called on each invocation 
    // to produce a new parameter instance. 
    internal class AsyncCollectorBinding<TAttribute, TMessage> : BindingBase<TAttribute>
        where TAttribute : Attribute
    {
        private readonly FuncArgumentBuilder<TAttribute> _argumentBuilder;

        public AsyncCollectorBinding(
            ParameterDescriptor param,
            FuncArgumentBuilder<TAttribute> argumentBuilder,
            AttributeCloner<TAttribute> cloner) 
            : base(cloner, param)
        {
            this._argumentBuilder = argumentBuilder;
        }

        protected override Task<IValueProvider> BuildAsync(TAttribute attrResolved, ValueBindingContext context)
        {
            var value = _argumentBuilder(attrResolved, context);
            return Task.FromResult(value);
        }      
    }
}