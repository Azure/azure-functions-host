// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using ParameterDescriptor = Microsoft.Azure.WebJobs.Host.Protocols.ParameterDescriptor;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class FunctionTraceBinderProvider
    {
        public static void Create(ExtensionConfigContext context)
        {
            var loggerFactory = context.Config.LoggerFactory;

            var traceBinderType = typeof(JobHostConfiguration).Assembly.GetType("Microsoft.Azure.WebJobs.Host.Bindings.TraceWriterBindingProvider");
            IBindingProvider inner = (IBindingProvider)Activator.CreateInstance(traceBinderType, loggerFactory);

            Func<string, FunctionDescriptor> funcLookup = context.Config.GetService<Func<string, FunctionDescriptor>>();

            if (funcLookup != null && inner != null)
            {
                IBindingProvider wrapper = new Wrapper(inner, funcLookup);

                var registry = context.Config.GetService<IExtensionRegistry>();
                registry.RegisterExtension(typeof(IBindingProvider), wrapper);
            }
        }

        private class Wrapper : IBindingProvider
        {
            private readonly IBindingProvider _inner;
            private readonly Func<string, FunctionDescriptor> _funcLookup;

            public Wrapper(
                IBindingProvider inner,
                Func<string, FunctionDescriptor> funcLookup)
            {
                _inner = inner;
                _funcLookup = funcLookup;
            }

            public async Task<IBinding> TryCreateAsync(BindingProviderContext context)
            {
                var result = await _inner.TryCreateAsync(context);
                if (result == null)
                {
                    return null;
                }

                return new WrapperBinding(result, this);
            }

            private class WrapperBinding : IBinding
            {
                private readonly IBinding _inner;
                private readonly Wrapper _parent;

                public WrapperBinding(IBinding inner, Wrapper parent)
                {
                    _inner = inner;
                    _parent = parent;
                }

                public bool FromAttribute => _inner.FromAttribute;

                public async Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
                {
                    var result = await _inner.BindAsync(value, context);
                    return await WrapAsync(result, context);
                }

                private async Task<IValueProvider> WrapAsync(IValueProvider result, ValueBindingContext context)
                {
                    var obj = await result.GetValueAsync();
                    if (obj is TraceWriter trace)
                    {
                        var shortName = context.FunctionContext.MethodName;
                        FunctionDescriptor descr = _parent._funcLookup(shortName);
                        var functionLogger = descr.Invoker.FunctionLogger;

                        // This is the critical call
                        trace = functionLogger.CreateUserTraceWriter(trace);

                        return new SimpleValueProvider(typeof(TraceWriter), trace, result.ToInvokeString());
                    }
                    return result;
                }

                public async Task<IValueProvider> BindAsync(Host.Bindings.BindingContext context)
                {
                    var result = await _inner.BindAsync(context);
                    return await WrapAsync(result, context.ValueContext);
                }

                public ParameterDescriptor ToParameterDescriptor()
                {
                    return _inner.ToParameterDescriptor();
                }
            }
        }
    }
}
