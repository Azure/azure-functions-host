using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using RunnerHost;
using SimpleBatch;

namespace SimpleBatch.Client
{
    // Implementation of ICall for local execution (great for unit testing)
    public class LocalCallImpl : CallImpl
    {
        private readonly Type _scope;
        private readonly CloudStorageAccount _account;

        public IConfiguration Configuration { get; private set; }

        public LocalCallImpl(CloudStorageAccount account, Type scope)
        {
            if (scope == null)
            {
                throw new ArgumentNullException("scope");
            }
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }
            _scope = scope;
            _account = account;

            // Initialize the configuration. Bind ICall to ourselves. 
            this.Configuration = RunnerHost.Program.InitBinders();
            this.Configuration.Binders.Insert(0, new LocalCallBinderProvider { _outer = this }); 
            RunnerHost.Program.ApplyHooks(scope, this.Configuration);
        }

        public class LocalCallBinderProvider : ICloudBinderProvider
        {
            public LocalCallImpl _outer;

            class CallBinder : ICloudBinder
            {
                public LocalCallImpl _outer;

                public BindResult Bind(IBinder bindingContext, System.Reflection.ParameterInfo parameter)
                {
                    return new BindResultTransaction
                    {
                        Result = _outer,
                        Cleanup = () =>
                        {
                            _outer.Flush();
                        }
                    };
                }
            }
            public ICloudBinder TryGetBinder(Type targetType)
            {
                if (targetType == typeof(ICall))
                {
                    return new CallBinder { _outer = _outer };
                }
                return null;
            }
        }
    

        // Rather than call to Website and queue over internet, just queue locally. 
        protected override Guid MakeWebCall(string functionShortName, IDictionary<string, string> parameters)
        {
            MethodInfo method = _scope.GetMethod(functionShortName, BindingFlags.Static | BindingFlags.Public);
            if (method == null)
            {
                string msg = string.Format("Can't resolve function '{0}' in type '{1}", functionShortName, _scope.FullName);
                throw new InvalidOperationException(msg);
            }

            // Runs synchronously
            Orchestrator.LocalOrchestrator.Invoke(_account, this.Configuration, method, parameters);

            return Guid.Empty;
        }

        protected override Task WaitOnCall(Guid g)
        {            
            // Since all calls here are sync, ignore the guid and just return task completed.
            return _completed;
        }

        static Task _completed = Completed();

        static Task Completed()
        {
            TaskCompletionSource<object> tsc = new TaskCompletionSource<object>();
            tsc.SetResult(null);
            return tsc.Task;
        }

    }
}
