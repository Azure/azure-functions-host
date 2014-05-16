using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindings
{
    internal class BinderParameterStaticBinding : ParameterStaticBinding
    {
        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            return new BinderParameterRuntimeBinding { Name = Name };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            return null;
        }

        public override ParameterDescriptor ToParameterDescriptor()
        {
            return new BinderParameterDescriptor();
        }

        private class BinderParameterRuntimeBinding : ParameterRuntimeBinding
        {
            public override string ConvertToInvokeString()
            {
                return null;
            }

            public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
            {
                var wrapper = new BinderWrapper(bindingContext);
                return new BindResult<IBinder>(wrapper)
                {
                    Cleanup = _ => wrapper.Cleanup()
                };
            }
        }

        // Wrap an IBinder to:
        // - ensure we cleanup all BindResults we hand out
        // - impl ISelfWatch so we can see all results we've handed out. 
        private class BinderWrapper : IBinder, ISelfWatch
        {
            // Track for cleanup
            private readonly List<BindResult> _results = new List<BindResult>();
            private readonly IBinderEx _inner;

            class Watches
            {
                public string Name;
                public ISelfWatch Watch;
            }
            private readonly List<Watches> _watches = new List<Watches>();

            public BinderWrapper(IBinderEx inner)
            {
                _inner = inner;
            }

            // Implements simplified IBinder instead of IBinderEx. Doesn't expose BindResult.
            public T Bind<T>(Attribute attribute)
            {
                var result = _inner.Bind<T>(attribute);

                // For selfwatch 
                Watches w = new Watches
                {
                    Name = attribute.ToString(),
                    Watch = SelfWatch.GetWatcher(result, typeof(T))
                };
                lock (_watches)
                {
                    _watches.Add(w);
                }

                _results.Add(result);
                return result.Result;
            }

            public string AccountConnectionString
            {
                get { return _inner.AccountConnectionString; }
            }

            public CancellationToken CancellationToken
            {
                get { return _inner.CancellationToken; }
            }

            public void Cleanup()
            {
                foreach (var result in _results)
                {
                    result.OnPostAction();
                }
            }

            public string GetStatus()
            {
                lock (_watches)
                {
                    if (_watches.Count == 0)
                    {
                        return String.Empty;
                    }

                    // Show selfwatch from objects we've handed out. 
                    StringBuilder sb = new StringBuilder();
                    sb.AppendFormat("Created {0} object(s):", _watches.Count);
                    sb.AppendLine();

                    foreach (var result in _watches)
                    {
                        sb.Append(result.Name);
                        if (result.Watch != null)
                        {
                            sb.Append(" ");
                            sb.Append(result.Watch.GetStatus());
                        }
                        sb.AppendLine();
                    }
                    return SelfWatch.EncodeSelfWatchStatus(sb.ToString());
                }
            }
        }
    }
}
