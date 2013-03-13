using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerHost;
using RunnerInterfaces;
using SimpleBatch;
using SimpleBatch.Client;

namespace Orchestrator
{
    // Provide in-memory settings that can glue an indexer, orchestrator, and execution.
    // Executes via in-memory MethodInfos without azure. 
    class IndexInMemory : IFunctionTable, IQueueFunction
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
            foreach (var method in _mapping)
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
            return new InMemoryFunctionLocation
            {
                Blob = new CloudBlobDescriptor
                {
                    AccountConnectionString = this.AccountConnectionString
                },
                MethodName = method.Name,
                TypeName = method.DeclaringType.Name,
                _method = method
            };
        }

        // Refer to a function via reflection, not necessarily that lives on a cloud blob. 
        // So blob information here may not be accurate. 
        class InMemoryFunctionLocation : FunctionLocation
        {
            public MethodInfo _method;

            public override string ReadFile(string filename)
            {
                var root = Path.GetDirectoryName(_method.DeclaringType.Assembly.Location);

                string path = Path.Combine(root, filename);
                return File.ReadAllText(path);
            }
        }

        void IFunctionTable.Add(FunctionIndexEntity func)
        {
            _funcs.Add(func);
        }

        void IFunctionTable.Delete(FunctionIndexEntity func)
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

        FunctionIndexEntity[] IFunctionTableLookup.ReadAll()
        {
            return _funcs.ToArray();
        }

        ExecutionInstanceLogEntity IQueueFunction.Queue(FunctionInvokeRequest instance)
        {
            InMemoryFunctionLocation location = (InMemoryFunctionLocation) instance.Location;
            MethodInfo m = location._method;

            // run immediately 
            IRuntimeBindingInputs inputs = new RuntimeBindingInputs(instance.Location);
            Program.Invoke(_config, m, instance.Id, inputs, instance.Args);

            return null;
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

        public FunctionIndexEntity Lookup(string functionId)
        {
            throw new NotImplementedException();
        }
    }
}