using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AzureTables;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Bind to IBinder
    // All return results get tracked on a cleanup list. 
    public class BinderBinderProvider : ICloudBinderProvider
    {
        // Wrap an IBinder to:
        // - ensure we cleanup all BindResults we hand out
        // - impl ISelfWatch so we can see all results we've handed out. 
        class BinderWrapper : IBinder, ISelfWatch
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
            public T Bind<T>(Attribute a)
            {
                var result = _inner.Bind<T>(a);

                // For selfwatch 
                Watches w = new Watches
                {
                    Name = a.ToString(),
                    Watch = Program.GetWatcher(result, typeof(T))
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
                    // Show selfwatch from objects we've handed out. 
                    StringBuilder sb = new StringBuilder();
                    foreach (var result in _watches)
                    {
                        sb.Append(result.Name);
                        if (result.Watch != null)
                        {
                            sb.Append(" ");
                            sb.Append(result.Watch.GetStatus());
                        }
                        sb.Append('.');
                        sb.AppendLine();
                    }
                    return sb.ToString();
                }
            }
        }

        class BinderBinder : ICloudBinder
        {
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                var wrapper = new BinderWrapper(bindingContext);
                return new BindResult<IBinder>(wrapper)
                {
                    Cleanup = _ => wrapper.Cleanup()
                };
            }
        }

        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(IBinder))
            {
                return new BinderBinder();
            }
            return null;
        }
    }
}