// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class TraceWriterBinding : IBinding
    {
        private readonly ParameterInfo _parameter;

        public TraceWriterBinding(ParameterInfo parameter)
        {
            _parameter = parameter;
        }

        public bool FromAttribute
        {
            get { return false; }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "TraceWriter")]
        public Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            if (value == null || !_parameter.ParameterType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Unable to convert value to {0}.", _parameter.ParameterType));
            }

            IValueProvider valueProvider = new ValueBinder(value, _parameter.ParameterType);
            return Task.FromResult<IValueProvider>(valueProvider);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Task<IValueProvider> BindAsync(BindingContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            object tracer = null;
            if (_parameter.ParameterType == typeof(TraceWriter))
            {
                // bind directly to the context TraceWriter
                tracer = context.Trace;
            }
            else
            {
                // bind to an adapter
                tracer = TextWriterTraceAdapter.Synchronized(context.Trace);
            }

            return BindAsync(tracer, context.ValueContext);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new ParameterDescriptor
            {
                Name = _parameter.Name
            };
        }

        private sealed class ValueBinder : IValueBinder
        {
            private readonly object _tracer;
            private readonly Type _type;

            public ValueBinder(object tracer, Type type)
            {
                _tracer = tracer;
                _type = type;
            }

            public Type Type
            {
                get { return _type; }
            }

            public Task<object> GetValueAsync()
            {
                return Task.FromResult(_tracer);
            }

            public string ToInvokeString()
            {
                return null;
            }

            public Task SetValueAsync(object value, CancellationToken cancellationToken)
            {
                TextWriterTraceAdapter traceAdapter = value as TextWriterTraceAdapter;
                if (traceAdapter != null)
                {
                    traceAdapter.Flush();
                }
                return Task.FromResult(0);
            }
        }
    }
}
