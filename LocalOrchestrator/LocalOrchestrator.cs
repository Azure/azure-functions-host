using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using AzureTables;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerHost;
using RunnerInterfaces;
using SimpleBatch;
using SimpleBatch.Client;

namespace Orchestrator
{
    public class LocalOrchestrator
    {
        // Build by indexing all methods in type
        public static Worker Build(CloudStorageAccount account, Type typeClass)
        {
            IndexInMemory store = new IndexInMemory(account);
            Indexer i = new Indexer(store);

            i.IndexType(store.OnApplyLocationInfo, typeClass);

            IFunctionTable functionTable = store;
            IQueueFunction executor = store;
            var worker = new Worker(functionTable, executor);
            return worker;        
        }

        // Run the method for the given blob parameter.
        public static void Invoke(CloudStorageAccount account, IConfiguration config, MethodInfo method, IDictionary<string, string> parameters)
        {
            InvokeWorker(account, method, config,
               func =>
               {
                   // No blob input information. 
                   return Worker.GetFunctionInvocation(func, parameters);
               });
        }

        public static void Invoke(CloudStorageAccount account, MethodInfo method, IDictionary<string, string> parameters)
        {
            InvokeWorker(account, method,
               func =>
               {
                   // No blob input information. 
                   return Worker.GetFunctionInvocation(func, parameters);
               });
        }

        // Run the method for the given blob parameter.
        public static void Invoke(CloudStorageAccount account, MethodInfo method)
        {
            InvokeWorker(account, method,
                func =>
                {
                    // No blob input information. 
                    return Worker.GetFunctionInvocation(func);
                });
        }


        public static void InvokeOnBlob(CloudStorageAccount account, IConfiguration config, MethodInfo method, string blobPath)
        {
            InvokeWorker(account, method, config,
                func =>
                {
                    CloudBlob blobInput = new CloudBlobPath(blobPath).Resolve(account);
                    return Worker.GetFunctionInvocation(func, blobInput);
                });
        }

        public static void InvokeOnBlob(CloudStorageAccount account, MethodInfo method, string blobPath)
        {
            InvokeWorker(account, method,
                func =>
                {
                    CloudBlob blobInput = new CloudBlobPath(blobPath).Resolve(account);
                    return Worker.GetFunctionInvocation(func, blobInput);
                });
        }

        // Common call point. 

        private static void InvokeWorker(CloudStorageAccount account, MethodInfo method, IConfiguration config, Func<FunctionDefinition, FunctionInvokeRequest> fpGetInstance)
        {            
            FunctionDefinition func = GetFunction(account, method);
            FunctionInvokeRequest instance = GetInstance(fpGetInstance, func);

            IRuntimeBindingInputs inputs = new RuntimeBindingInputs(instance.Location);
            Program.Invoke(config, method, instance.Id, inputs, instance.Args);
        }

        private static void InvokeWorker(CloudStorageAccount account, MethodInfo method, Func<FunctionDefinition, FunctionInvokeRequest> fpGetInstance)
        {
            var config = ReflectionFunctionInvoker.GetConfiguration(account, method.DeclaringType);
            InvokeWorker(account, method, config, fpGetInstance);
        }

        private static FunctionInvokeRequest GetInstance(Func<FunctionDefinition, FunctionInvokeRequest> fpGetInstance, FunctionDefinition func)
        {
            var instance = fpGetInstance(func);
            instance.Id = Guid.NewGuid(); // add the function instance id for causality tracking
            return instance;
        }

        // Convert MethodInfo --> FunctionIndexEntity
        internal static FunctionDefinition GetFunction(CloudStorageAccount account, MethodInfo method)
        {
            IndexInMemory store = new IndexInMemory(account);
            Indexer i = new Indexer(store);

            i.IndexMethod(store.OnApplyLocationInfo, method);

            IFunctionTable functionTable = store;
            var funcs = functionTable.ReadAll();
            FunctionDefinition func = funcs[0];
            return func;
        }
    }

    // Replumb LocalOrch on this. 
    class LocalExecutionContext
    {        
        private readonly IConfiguration _config;
        private readonly IPrereqManager _prereq;
        private readonly IQueueFunction _queueFunction;
        private readonly IActivateFunction _activator;
        private readonly IFunctionInstanceLookup _lookup;
        private readonly IFunctionUpdatedLogger _functionUpdate;

        private readonly Func<MethodInfo, FunctionDefinition> _fpResolveFuncDefinition;

        // Scope is for invokgin on ICall. 
        public LocalExecutionContext(CloudStorageAccount account, Type scope, IConfiguration config)
        {
            // 1. New up LocalFunctionInvoker, and pass this object in. USe in MakeWebCall
            // 2. 
            _fpResolveFuncDefinition = method => Resolve(account, config, method);

            // NEed Config that binds ICall back to invoke here. 
            //_config = ReflectionFunctionInvoker.GetConfiguration(account, scope);
            _config = config; // !!!

            //var table = AzureTable<ExecutionInstanceLogEntity>.NewInMemory(); !!!
            //var x = new FunctionUpdatedLogger(table);
            var x = new LocalFunctionLogger();
            _functionUpdate = x;
            _lookup = x;

            IAzureTable prereqTable = AzureTable.NewInMemory();
            IAzureTable successorTable = AzureTable.NewInMemory();

            _prereq = new PrereqManager(prereqTable, successorTable, _lookup);

            var qi = new QueueInterfaces
            {
                AccountInfo = new AccountInfo(), // For webdashboard. NA in local case
                Logger = _functionUpdate,
                Lookup = _lookup,
                PreqreqManager = _prereq,
                CausalityLogger = new EmptyCausality()
            };

            var y = new LocalQueue(qi, this);
            _queueFunction = y;
            _activator = y;
        }

        private static FunctionDefinition Resolve(CloudStorageAccount account, IConfiguration config, MethodInfo method)
        {
            IndexInMemory store = new IndexInMemory(account, config);
            Indexer i = new Indexer(store);

            i.IndexMethod(store.OnApplyLocationInfo, method);

            IFunctionTable functionTable = store;
            var funcs = functionTable.ReadAll();
            FunctionDefinition func = funcs[0];
            return func;
        }

        // Binds 
        // !!! If no prereqs, can run immediately. 
        public Guid Call(MethodInfo method, IDictionary<string, string> parameters, IEnumerable<Guid> prereqs)
        {
            var func = _fpResolveFuncDefinition(method);
            FunctionInvokeRequest instance = Worker.GetFunctionInvocation(func, parameters, prereqs);
            instance.TriggerReason = new InvokeTriggerReason { Message = "Local invoke" };
            // !!! include parent guid?

            var logItem = _queueFunction.Queue(instance);
            var guid = logItem.FunctionInstance.Id;

            return guid;
        }

        class EmptyCausality : ICausalityLogger
        {
            public void LogTriggerReason(TriggerReason reason)
            {
                // Ignored.
            }
        }

        class LocalQueue : QueueFunctionBase, IActivateFunction
        {
            LocalExecutionContext _parent;

            public LocalQueue(QueueInterfaces interfaces, LocalExecutionContext parent)
                : base(interfaces)
            {
                _parent = parent;
            }

            protected override void Work(ExecutionInstanceLogEntity logItem)
            {
                // Run the function. 
                // The config is what will have the ICall binder that ultimately points back to this object. 
                Program.Invoke(logItem.FunctionInstance, _parent._config);

                // Mark this function as done executing. !!! Merge with ExecutionBase?
                logItem.EndTime = DateTime.UtcNow;
                _parent._functionUpdate.Log(logItem);

                // Now execute any successors that were queued up. 
                var guid = logItem.FunctionInstance.Id;
                _parent._prereq.OnComplete(guid, _parent._activator);
            }
        }


        // Ideally use FunctionUpdatedLogger with in-memory azure tables (this would minimize code deltas).
        // For local execution, we may have function objects that don't serialize. So we can't run through azure tables.
        // $$$ Better way to merge?
        class LocalFunctionLogger : IFunctionUpdatedLogger, IFunctionInstanceLookup
        {
            Dictionary<string, ExecutionInstanceLogEntity> _dict = new Dictionary<string, ExecutionInstanceLogEntity>();

            void IFunctionUpdatedLogger.Log(ExecutionInstanceLogEntity log)
            {
                string rowKey = log.GetKey();

                var l2 = this.Lookup(rowKey);
                if (l2 == null)
                {
                    l2 = log;
                }
                else
                {
                    // Merge
                    FunctionUpdatedLogger.Merge(l2, log);
                }

                _dict[rowKey] = l2;
            }

            ExecutionInstanceLogEntity IFunctionInstanceLookup.Lookup(Guid rowKey)
            {
                return Lookup(rowKey.ToString());
            }

            private ExecutionInstanceLogEntity Lookup(string rowKey)
            {
                ExecutionInstanceLogEntity log;
                _dict.TryGetValue(rowKey, out log);
                return log;
            }
        }
    }
}
