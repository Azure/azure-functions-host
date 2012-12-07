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
            private readonly IBinder _inner;

            class Watches
            {
                public string Name;                
                public ISelfWatch Watch;
            }
            private readonly List<Watches> _watches = new List<Watches>();

            public BinderWrapper(IBinder inner)
            {
                _inner = inner;
            }

            public BindResult<T> Bind<T>(Attribute a)
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
                return result;
            }

            public string AccountConnectionString
            {
                get { return _inner.AccountConnectionString; }
            }

            public void Cleanup()
            { 
                foreach(var result in _results)
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
            public BindResult Bind(IBinder bindingContext, ParameterInfo parameter)
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


    class BindingContext : IBinder
    {
        private string _accountConnectionString;
        private IConfiguration _config;
        public BindingContext(IConfiguration config, string accountConnectionString)
        {
            _config = config;
            _accountConnectionString = accountConnectionString;
        }

        // optionally pass in names, which flow to RuntimeBindingInputs?
        // names would just resolve against {} tokens in attributes?
        public BindResult<T> Bind<T>(Attribute a)
        {
            // Always bind as input parameters, eg no 'out' keyword. 
            // The binding could still have output semantics. Eg, bind to a TextWriter. 
            ParameterInfo p = new FakeParameterInfo(typeof(T), name: "?", isOut: false);

            // Same static binding as used in indexing
            ParameterStaticBinding staticBind = StaticBinder.DoStaticBind(a, p); 

            RuntimeBindingInputs inputs = new RuntimeBindingInputs
            { 
                 _account = Utility.GetAccount(_accountConnectionString)
            };

            // If somebody tried an non-sensical bind, we'd get the failure here 
            // here because the binding input doesn't have the information. 
            // Eg, eg a QueueInput attribute would fail because input doesn't have a queue input message.
            ParameterRuntimeBinding runtimeBind = staticBind.Bind(inputs);

            BindResult result = runtimeBind.Bind(_config, this, p);
            return Utility.StrongWrapper<T>(result);                
        }
        
        public string AccountConnectionString
        {
            get { return _accountConnectionString; }
        }
    }


    public class CollisionDetector
    {
        // ### We don't have the Static binders...
        // Throw if binds read and write to the same resource. 
        public static void DetectCollisions(BindResult[] binds)
        {
        }
    }
}