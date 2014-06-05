using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    // Support local execution. This does not have a trigger service, but still maintains all of the logging and causality.
    // Exposes some of the logging objects so that callers can monitor what happened. 
    internal class LocalExecutionContext
    {
        private readonly CloudStorageAccount _account;
        private readonly IConfiguration _config;
        private readonly IExecuteFunction _executor;
        private readonly IFunctionInstanceLookup _lookup;
        private readonly IFunctionUpdatedLogger _functionUpdate;
        private readonly RuntimeBindingProviderContext _context;

        private readonly Func<MethodInfo, FunctionDefinition> _fpResolveFuncDefinition;
        private readonly Func<string, MethodInfo> _fpResolveMethod;
        private readonly Func<string, CloudBlockBlob> _fpResolveBlobs;

        public IFunctionInstanceLookup FunctionInstanceLookup
        {
            get
            {
                return _lookup;
            }
        }

        // Expose to allow callers to hook in new binders. 
        public IConfiguration Configuration
        {
            get
            {
                return _config;
            }
        }

        private static IConfiguration CreateConfig(Type scope)
        {
            IConfiguration config = new Configuration();

            JobHostContext.AddCustomerBinders(config, new Type[] { scope });

            return config;
        }

        public LocalExecutionContext(string accountConnectionString, Type scope)
        {
            _account = CloudStorageAccount.Parse(accountConnectionString);

            _config = CreateConfig(scope);

            // These resolution functions are "open". We lazily resolve to any method on the scope type. 
            _fpResolveMethod = name => Resolve(scope, name); // string-->MethodInfo
            _fpResolveFuncDefinition = method => Resolve(_account, _config, method); // MethodInfo-->FunctionDefinition

            _fpResolveBlobs = blobPath => (CloudBlockBlob)new CloudBlobPath(blobPath).Resolve(_account);

            {
                var x = new LocalFunctionLogger();
                _functionUpdate = x;
                _lookup = x;
            }

            var y = new LocalExecute(this);
            _executor = y;

            _context = new RuntimeBindingProviderContext
            {
                CancellationToken = CancellationToken.None,
                StorageAccount = _account,
                BindingProvider = DefaultBindingProvider.Create(null)
            };
        }

        private static MethodInfo Resolve(Type scope, string functionShortName)
        {
            MethodInfo method = scope.GetMethod(functionShortName, BindingFlags.Static | BindingFlags.Public);
            if (method == null)
            {
                string msg = string.Format("Can't resolve function '{0}' in type '{1}", functionShortName, scope.FullName);
                throw new InvalidOperationException(msg);
            }
            return method;
        }

        private static FunctionDefinition Resolve(CloudStorageAccount account, IConfiguration config, MethodInfo method)
        {
            LocalFunctionTable store = new LocalFunctionTable(account);
            Indexer i = new Indexer(store, config.NameResolver, config.CloudBlobStreamBinderTypes, account, null);

            i.IndexMethod(method);

            IFunctionTable functionTable = store;
            var funcs = functionTable.ReadAll();

            if (funcs.Length == 0)
            {
                string msg = string.Format("Function '{0}' is not found. Is it missing Azure Jobs attributes?", method);
                throw new InvalidOperationException(msg);
            }

            FunctionDefinition func = funcs[0];
            return func;
        }

        private FunctionDefinition ResolveFunctionDefinition(string functionName)
        {
            var methodInfo = _fpResolveMethod(functionName);
            return ResolveFunctionDefinition(methodInfo);
        }

        private FunctionDefinition ResolveFunctionDefinition(MethodInfo methodInfo)
        {
            return _fpResolveFuncDefinition(methodInfo);
        }

        public Guid Call(string functionName, object arguments = null)
        {
            var args2 = ObjectBinderHelpers.ConvertObjectToDict(arguments);
            MethodInfo method = _fpResolveMethod(functionName);

            var guid = Call(method, args2);
            return guid;
        }

        // If no prereqs, can run immediately. 
        public Guid Call(MethodInfo method, IDictionary<string, string> parameters = null)
        {
            FunctionDefinition func = ResolveFunctionDefinition(method);
            IDictionary<string, object> objectParameters = new Dictionary<string, object>();

            if (parameters != null)
            {
                foreach (KeyValuePair<string, string> item in parameters)
                {
                    objectParameters.Add(item.Key, item.Value);
                }
            }

            FunctionInvokeRequest instance = Worker.GetFunctionInvocation(func, objectParameters, _context);

            Guid guidThis = CallUtil.GetParentGuid(parameters);
            return CallInner(instance, guidThis);
        }

        private void CallInner(FunctionInvokeRequest instance)
        {
            CallInner(instance, Guid.Empty);
        }
        private Guid CallInner(FunctionInvokeRequest instance, Guid parentGuid)
        {
            instance.TriggerReason = new InvokeTriggerReason
            {
                Message = "Local invoke",
                ParentGuid = parentGuid
            };

            var result = _executor.Execute(instance, _context);
            return result.Id;
        }

        public void CallOnBlob(string functionName, string blobPath)
        {
            CloudBlockBlob blobInput = _fpResolveBlobs(blobPath);
            FunctionDefinition func = ResolveFunctionDefinition(functionName);
            FunctionInvokeRequest instance = Worker.GetFunctionInvocation(func, _context, blobInput);

            CallInner(instance);
        }

        class LocalExecute : IExecuteFunction
        {
            private readonly LocalExecutionContext _parent;

            public LocalExecute(LocalExecutionContext parent)
            {
                _parent = parent;
            }

            public FunctionInvocationResult Execute(FunctionInvokeRequest instance, RuntimeBindingProviderContext context)
            {
                if (instance.TriggerReason == null)
                {
                    // Having a trigger reason is important for diagnostics. 
                    // So make sure it's not accidentally null. 
                    throw new InvalidOperationException("Function instance must have a trigger reason set.");
                }
                instance.TriggerReason.ChildGuid = instance.Id;

                var logItem = new ExecutionInstanceLogEntity();
                logItem.FunctionInstance = instance;
                bool succeeded;

                MethodInfo method = instance.Method;

                // Run the function. 
                // The config is what will have the ICall binder that ultimately points back to this object. 
                try
                {
                    WebSitesExecuteFunction.ExecuteWithSelfWatch(method, method.GetParameters(), instance.ParametersProvider.Bind(), TextWriter.Null);
                    succeeded = true;
                }
                catch (Exception e)
                {
                    logItem.ExceptionType = e.GetType().FullName;
                    logItem.ExceptionMessage = e.Message;
                    succeeded = false;
                }

                // Mark this function as done executing. $$$ Merge with ExecutionBase?
                logItem.EndTime = DateTime.UtcNow;
                _parent._functionUpdate.Log(logItem);

                return new FunctionInvocationResult
                {
                    Id = instance.Id,
                    Succeeded = succeeded,
                    ExceptionInfo = new DelayedException(new Exception(logItem.ExceptionMessage))
                };
            }
        }
    }
}
