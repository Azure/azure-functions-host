// @@@ This needs to get factored out into a nuget package and be reusable. 

using Executor;
using RunnerInterfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Orchestrator;

// @@@ Merge with ones in C:\codeplex\azuresimplebatch\WebFrontEnd\Antares\CodeFile1.cs 
namespace SimpleBatch.Internals
{
    // In-memory executor. 
    class AntaresQueueFunction : QueueFunctionBase
    {
        // Logging hook for each function invoked. 
        private readonly Action<FunctionInvokeRequest> _fpLog;

        private readonly FunctionExecutionContext _ctx;

        private readonly IConfiguration _config;

        public AntaresQueueFunction(QueueInterfaces interfaces, IConfiguration config, FunctionExecutionContext ctx, Action<FunctionInvokeRequest> fpLog = null)
            : base(interfaces)
        {
            _config = config;
            _fpLog = fpLog;
            _ctx = ctx;
        }
        protected override void Work(ExecutionInstanceLogEntity logItem)
        {
            var request = logItem.FunctionInstance;
            var loc = request.Location;



            Func<TextWriter, FunctionExecutionResult> fpInvokeFunc =
                (consoleOutput) =>
                {
                    if (_fpLog != null)
                    {
                        _fpLog(request);
                    }
                    
                    // @@@ May need to be in a new appdomain. 
                    var oldOutput = Console.Out;
                    Console.SetOut(consoleOutput);

                    // @@@ May need to override config to set ICall
                    var result = RunnerHost.Program.MainWorker(request, _config);
                    Console.SetOut(oldOutput);
                    return result;
                };

            // @@@ somewhere this should be async, handle long-running functions. 
            ExecutionBase.Work(
                request,
                _ctx,
                fpInvokeFunc);
        }
    }

    // $$$ Merge with Orch's IndexInMemory 
    class IndexInMemory : IFunctionTable
    {
        List<FunctionDefinition> List = new List<FunctionDefinition>();

        public void Add(FunctionDefinition func)
        {
            List.Add(func);
        }

        void IFunctionTable.Delete(FunctionDefinition func)
        {
            string funcString = func.ToString();
            foreach (var x in List)
            {
                if (x.ToString() == funcString)
                {
                    List.Remove(x);
                    return;
                }
            }
        }

        public FunctionDefinition Lookup(string functionId)
        {
            // $$$ Not linear :(
            foreach (var x in List)
            {
                if (x.Location.ToString() == functionId)
                {
                    return x;
                }
            }
            return null;
        }

        public FunctionDefinition[] ReadAll()
        {
            return List.ToArray();
        }

    }

    class FunctionStore : IFunctionTableLookup
    {
        IndexInMemory _store;

        string _prefix;

        // userAccountConnectionString - the account that the functions will bind against. 
        public FunctionStore(string userAccountConnectionString, IEnumerable<Assembly> assemblies, IConfiguration config)
        {
            _prefix = GetPrefix(userAccountConnectionString);

            _store = new IndexInMemory();
            var indexer = new Indexer(_store);
            indexer._configOverride = config;

            foreach (Assembly a in assemblies)
            {
                indexer.IndexAssembly(m => OnApplyLocationInfo(userAccountConnectionString, m), a);
            }
        }

        // Get a prefix for function IDs. 
        // This is particularly important when we delete stale functions. Needs to be specific
        // enough so we don't delete other user's / other assembly functions.
        // Multiple users may share the same backing logging,
        // and a single user may have multiple assemblies. 
        internal static string GetPrefix(string accountConnectionString)
        {
            string appName = "_";
            var a = Assembly.GetEntryAssembly();
            if (a != null)
            {
                appName = Path.GetFileNameWithoutExtension(a.Location);
            }
        
            //%USERNAME% is too volatile. Instead, get identity based on the user's storage account name.
            //string userName = Environment.GetEnvironmentVariable("USERNAME") ?? "_";
            string accountName = Utility.GetAccountName(accountConnectionString);

            return accountName + "." + appName;
        }

        FunctionLocation OnApplyLocationInfo(string accountConnectionString, MethodInfo method)
        {
            var loc = new MethodInfoFunctionLocation(method)
            {
                AccountConnectionString = accountConnectionString,
            };

            loc.Id = _prefix + "." + loc.Id;
            return loc;
        }


        public FunctionDefinition Lookup(string functionId)
        {
            IFunctionTableLookup x = _store;
            return x.Lookup(functionId);
        }

        public FunctionDefinition[] ReadAll()
        {
            IFunctionTableLookup x = _store;
            return x.ReadAll();
        }
    }
}