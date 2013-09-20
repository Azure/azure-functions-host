using System;
using System.Collections.Generic;
using System.Configuration;
using RunnerHost;
using RunnerInterfaces;

namespace SimpleBatch
{
    // Provide an easy binder for clients.
    // this can let client leverage the SimpleBatch programming model,
    // and also exposes simple batch adapters so that clients can directly invoke simpleBatch functions.
    // $$$ Share with RunnerHost!BinderWrapper?
    public class Binder : IBinder, IDisposable
    {
        private readonly string _accountConnectionString;
        private readonly IBinderEx _inner;

        // Track for cleanup
        private readonly List<BindResult> _results = new List<BindResult>();

        public Binder()
            : this(GetDefaultConnectionString())
        {
        }

        private static string GetDefaultConnectionString()
        {
            // By default, use the one supplied in Web.Config, which is what the indexing actions will use. 
            return ConfigurationManager.AppSettings["SimpleBatchAccountConnectionString"]; 
        }

        public Binder(string accountConnectionString)
        {
            _accountConnectionString = accountConnectionString;

            IConfiguration config = RunnerHost.Program.InitBinders();
            IRuntimeBindingInputs runtimeInputs = new RuntimeBindingInputs(accountConnectionString);
            var functionInstance = Guid.Empty;

            _inner = new BindingContext(config, runtimeInputs, functionInstance, notificationService : null);
        }

        public T Bind<T>(Attribute a)
        {
            var result = _inner.Bind<T>(a);
            _results.Add(result);
            return result.Result;
        }

        public string AccountConnectionString
        {
            get { return _accountConnectionString;  }
        }

        public void Dispose()
        {
            foreach (var result in _results)
            {
                result.OnPostAction();
            }
        }
    }
}