using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Orchestrator;
using RunnerHost;
using SimpleBatch;

namespace SimpleBatch.Client
{
    // Implementation of ICall for local execution (great for unit testing)
    public class ReflectionFunctionInvoker : LocalFunctionInvoker
    {
        private readonly Type _scope;

        public ReflectionFunctionInvoker(CloudStorageAccount account, Type scope) 
            : base(account, scope)
        {
            if (scope == null)
            {
                throw new ArgumentNullException("scope");
            }

            _scope = scope;
        }
        
        protected override MethodInfo ResolveMethod(string functionShortName)
        {
            MethodInfo method = _scope.GetMethod(functionShortName, BindingFlags.Static | BindingFlags.Public);
            if (method == null)
            {
                string msg = string.Format("Can't resolve function '{0}' in type '{1}", functionShortName, _scope.FullName);
                throw new InvalidOperationException(msg);
            }
            return method;
        }

        // Initialize the configuration. Bind ICall to ourselves. 
        public static IConfiguration GetConfiguration(CloudStorageAccount account, Type scope)
        {
            var caller = new ReflectionFunctionInvoker(account, scope);
            return caller.Configuration;
        }
    }

    public abstract class LocalFunctionInvoker : FunctionInvoker
    {
        private readonly CloudStorageAccount _account;

        public IConfiguration Configuration { get; private set; }

        LocalExecutionContext _localExec;

        public LocalFunctionInvoker(CloudStorageAccount account, Type scope)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }
            _account = account;

            this.Configuration = GetConfiguration(scope, this);

            _localExec = new LocalExecutionContext(account, scope, this.Configuration);
        }

        public static IConfiguration GetConfiguration(Type scope, LocalFunctionInvoker caller)
        {
            if (caller == null)
            {
                throw new ArgumentNullException("caller");
            }

            var config = RunnerHost.Program.InitBinders();
            InsertCallBinderProvider(caller, config);

            if (scope != null)
            {
                RunnerHost.Program.ApplyHooks(scope, config);
            }

            return config;
        }

        public static void InsertCallBinderProvider(LocalFunctionInvoker caller, IConfiguration config)
        {
            CallBinderProvider.Insert( () => caller, config);
        }

        protected abstract MethodInfo ResolveMethod(string functionShortName);

        // Rather than call to Website and queue over internet, just queue locally. 
        protected override Guid MakeWebCall(string functionShortName, IDictionary<string, string> parameters, IEnumerable<Guid> prereqs)
        {
            var method = ResolveMethod(functionShortName);

            var guid = _localExec.Call(method, parameters, prereqs);
            return guid;


#if false // !!!
            if (prereqs != null && prereqs.Any())
            {
                throw new NotImplementedException("This implementation of ICall does not support prerequisites");
            }

            var method = ResolveMethod(functionShortName);

            // Runs synchronously // !!!
            Orchestrator.LocalOrchestrator.Invoke(_account, this.Configuration, method, parameters);

            return Guid.Empty;
#endif
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
