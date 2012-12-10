using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
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

            var worker = new Worker(store);
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

        private static void InvokeWorker(CloudStorageAccount account, MethodInfo method, IConfiguration config, Func<FunctionIndexEntity, FunctionInvokeRequest> fpGetInstance)
        {            
            FunctionIndexEntity func = GetFunction(account, method);
            FunctionInvokeRequest instance = fpGetInstance(func);
            RunnerHost.Program.Invoke(config, method, instance.Args);
        }

        private static void InvokeWorker(CloudStorageAccount account, MethodInfo method, Func<FunctionIndexEntity, FunctionInvokeRequest> fpGetInstance)
        {
            FunctionIndexEntity func = GetFunction(account, method);
            FunctionInvokeRequest instance = fpGetInstance(func);

            var config = ReflectionFunctionInvoker.GetConfiguration(account, method.DeclaringType);

            RunnerHost.Program.Invoke(config, method, instance.Args);
        }

        // Convert MethodInfo --> FunctionIndexEntity
        private static FunctionIndexEntity GetFunction(CloudStorageAccount account, MethodInfo method)
        {
            IndexInMemory store = new IndexInMemory(account);
            Indexer i = new Indexer(store);

            i.IndexMethod(store.OnApplyLocationInfo, method);

            var funcs = ((IOrchestratorSettings)store).ReadFunctionTable();
            FunctionIndexEntity func = funcs[0];
            return func;
        }
    }

    // Provide in-memory settings that can glue an indexer, orchestrator, and execution.
    // Executes via in-memory MethodInfos without azure. 
    class IndexInMemory : IIndexerSettings, IOrchestratorSettings
    {
        List<FunctionIndexEntity> _funcs = new List<FunctionIndexEntity>();
        List<MethodInfo> _mapping = new List<MethodInfo>();
        
        public IndexInMemory(CloudStorageAccount account)
        {
            this.Account = account;
            this.AccountConnectionString = Utility.GetConnectionString(account);

            _config = RunnerHost.Program.InitBinders();
            var caller = new InMemoryIndexFunctionInvoker(this);
            LocalFunctionInvoker.InsertCallBinderProvider(caller, _config);
        }

        private IConfiguration _config;

        public CloudStorageAccount Account { get; private set; }
        public string AccountConnectionString { get; private set; }

        public MethodInfo GetMethod(string functionShortName)
        {
            foreach(var method in _mapping)
            {
                if (method.Name == functionShortName)
                {
                    return method;
                }
            }
            string msg = string.Format("Can't resolve function '{0}'.", functionShortName);
            throw new InvalidOperationException(msg); 
        }

        public FunctionLocation OnApplyLocationInfo(MethodInfo method)
        {
            int idx = _mapping.Count;
            _mapping.Add(method);

            // Still need account information because blob inputs are relative to these accounts.
            return new FunctionLocation
            {
                Blob = new CloudBlobDescriptor
                {
                     AccountConnectionString = this.AccountConnectionString
                },
                MethodName = method.Name,
                TypeName = idx.ToString()
            };
        }

        void IIndexerSettings.Add(FunctionIndexEntity func)
        {
            _funcs.Add(func);
        }

        void IIndexerSettings.CleanFunctionIndex()
        {
            throw new NotImplementedException();
        }

        void IIndexerSettings.Delete(FunctionIndexEntity func)
        {
            string funcString = func.ToString();
            foreach (var x in _funcs)
            {
                if (x.ToString() == funcString)
                {
                    _funcs.Remove(x);
                    return;
                }
            }
        }

        FunctionIndexEntity[] IIndexerSettings.ReadFunctionTable()
        {
            return _funcs.ToArray();
        }

        FunctionIndexEntity[] IOrchestratorSettings.ReadFunctionTable()
        {
            return _funcs.ToArray();
        }

        void IOrchestratorSettings.QueueFunction(FunctionInvokeRequest instance)
        {
            int idx = int.Parse(instance.Location.TypeName);
            MethodInfo m = _mapping[idx];
                                    
            // run immediately 
            RunnerHost.Program.Invoke(_config, m, instance.Args);
        }


        public DateTime? GetLastExecutionTime(FunctionLocation func)
        {
            return null;
        }


        public void RequestBinder(Type t)
        {
            // Nop. In-memory doesn't pull binders down from cloud. 
            // Binders must be already set in the IConfiguration
        }
    }


    class InMemoryIndexFunctionInvoker : LocalFunctionInvoker
    {
        private readonly IndexInMemory _indexer;

        public InMemoryIndexFunctionInvoker(IndexInMemory indexer)
             : base(indexer.Account, null) 
        {
            // null scope means we don't invoke hooks.
            _indexer = indexer;
        }

        protected override MethodInfo ResolveMethod(string functionShortName)
        {
            return _indexer.GetMethod(functionShortName);
        }
    }

}