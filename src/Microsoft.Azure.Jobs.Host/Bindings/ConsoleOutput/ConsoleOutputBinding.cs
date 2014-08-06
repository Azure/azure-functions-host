// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.ConsoleOutput
{
    internal class ConsoleOutputBinding : IBinding
    {
        private readonly string _parameterName;

        public ConsoleOutputBinding(string parameterName)
        {
            _parameterName = parameterName;
        }

        public bool FromAttribute
        {
            get { return false; }
        }

        private Task<IValueProvider> BindAsync(TextWriter writer, ValueBindingContext context)
        {
            IValueProvider provider = new ConsoleOutputValueProvider(writer);
            return Task.FromResult(provider);
        }

        public Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            TextWriter writer = value as TextWriter;

            if (writer == null)
            {
                throw new InvalidOperationException("Unable to convert value to console output TextWriter.");
            }

            return BindAsync(writer, context);
        }

        public Task<IValueProvider> BindAsync(BindingContext context)
        {
            return BindAsync(context.ConsoleOutput, context.ValueContext);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new ConsoleOutputParameterDescriptor
            {
                Name = _parameterName
            };
        }
    }
}
