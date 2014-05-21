using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using AzureTables;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    // Support local execution. This does not have a trigger service, but still maintains all of the logging and causality.
    // Exposes some of the logging objects so that callers can monitor what happened. 
    internal class LocalExecutionContext
    {
        private readonly IConfiguration _config;
        private readonly IExecuteFunction _executor;
        private readonly IFunctionInstanceLookup _lookup;
        private readonly IFunctionUpdatedLogger _functionUpdate;

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
            IConfiguration config = RunnerProgram.InitBinders();

            if (scope != null)
            {
                RunnerProgram.ApplyHooks(scope, config);
            }

            JobHostContext.AddCustomerBinders(config, new Type[] { scope } );

            return config;
        }

        public LocalExecutionContext(string accountConnectionString, Type scope)
        {
            var account = CloudStorageAccount.Parse(accountConnectionString);

            _config = CreateConfig(scope);

            // These resolution functions are "open". We lazily resolve to any method on the scope type. 
            _fpResolveMethod = name => Resolve(scope, name); // string-->MethodInfo
            _fpResolveFuncDefinition = method => Resolve(account, _config, method); // MethodInfo-->FunctionDefinition

            _fpResolveBlobs = blobPath => (CloudBlockBlob)new CloudBlobPath(blobPath).Resolve(account);

            {
                var x = new LocalFunctionLogger();
                _functionUpdate = x;
                _lookup = x;
            }

            IAzureTable prereqTable = AzureTable.NewInMemory();
            IAzureTable successorTable = AzureTable.NewInMemory();

            var y = new LocalExecute(this);
            _executor = y;
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
            Indexer i = new Indexer(store);

            IndexTypeContext ctx = new IndexTypeContext { Config = config };
            i.IndexMethod(store.OnApplyLocationInfo, method, ctx);

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
            FunctionInvokeRequest instance = Worker.GetFunctionInvocation(func, parameters);

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

            var result = _executor.Execute(instance, CancellationToken.None);
            return result.Id;
        }

        public void CallOnBlob(string functionName, string blobPath)
        {
            CloudBlockBlob blobInput = _fpResolveBlobs(blobPath);
            FunctionDefinition func = ResolveFunctionDefinition(functionName);
            FunctionInvokeRequest instance = Worker.GetFunctionInvocation(func, blobInput);

            CallInner(instance);
        }

        class LocalExecute : ExecuteFunctionBase
        {
            private readonly LocalExecutionContext _parent;

            public LocalExecute(LocalExecutionContext parent)
            {
                _parent = parent;
            }

            protected override FunctionInvocationResult Work(FunctionInvokeRequest instance, CancellationToken cancellationToken)
            {
                RunnerProgram runner = new RunnerProgram(TextWriter.Null, null);

                var logItem = new ExecutionInstanceLogEntity();
                logItem.FunctionInstance = instance;
                bool succeeded;

                // Run the function. 
                // The config is what will have the ICall binder that ultimately points back to this object. 
                try
                {
                    runner.Invoke(instance, _parent._config, cancellationToken);
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
                    ExceptionMessage = logItem.ExceptionMessage
                };
            }
        }
    }
}
