// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.Cancellation
{
    internal class CancellationTokenBinding : IBinding
    {
        private readonly string _parameterName;

        public CancellationTokenBinding(string parameterName)
        {
            _parameterName = parameterName;
        }

        public bool FromAttribute
        {
            get { return false; }
        }

        private IValueProvider Bind(CancellationToken token, FunctionBindingContext context)
        {
            return new CancellationTokenValueProvider(token);
        }

        public IValueProvider Bind(object value, FunctionBindingContext context)
        {
            if (value is CancellationToken)
            {
                throw new InvalidOperationException("Unable to convert value to CancellationToken.");
            }

            CancellationToken token = (CancellationToken)value;

            return Bind(token, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(context.CancellationToken, context.FunctionContext);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new CancellationTokenParameterDescriptor
            {
                Name = _parameterName
            };
        }
    }
}
