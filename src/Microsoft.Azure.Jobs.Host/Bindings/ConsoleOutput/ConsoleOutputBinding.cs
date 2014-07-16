// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
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

        private IValueProvider Bind(TextWriter writer, FunctionBindingContext context)
        {
            return new ConsoleOutputValueProvider(writer);
        }

        public IValueProvider Bind(object value, FunctionBindingContext context)
        {
            TextWriter writer = value as TextWriter;

            if (writer == null)
            {
                throw new InvalidOperationException("Unable to convert value to console output TextWriter.");
            }

            return Bind(writer, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(context.ConsoleOutput, context.FunctionContext);
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
