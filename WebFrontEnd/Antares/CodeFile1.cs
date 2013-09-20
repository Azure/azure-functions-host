// @@@ This needs to get factored out into a nuget package and be reusable. 

using DaasEndpoints;
using Executor;
using Ninject;
using Orchestrator;
using RunnerInterfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using WebFrontEnd.Configuration;

namespace WebFrontEnd
{
    class SimpleBatchStuff
    {
        public static bool BadInit;
        public static bool Init(IKernel kernel)
        {            
            // Load user assemblies. 
            // @@@ already done
            var config = kernel.Get<ConfigurationService>();
            
            string accountConnectionString = config.ReadSetting("AzureStorage");
            if (string.IsNullOrWhiteSpace(accountConnectionString))
            {
                return false;
            }

            // Reflect over assembly, looking for functions 
            IFunctionTableLookup functionTableLookup = new FunctionStore(accountConnectionString);

            // Queue interfaces
            var services = kernel.Get<Services>();
            QueueInterfaces qi = services.GetQueueInterfaces(); // All for logging. 

            string roleName = "antares:" + Process.GetCurrentProcess().Id.ToString();
            var logger = new WebExecutionLogger(services, LogRole, roleName);
            var ctx = logger.GetExecutionContext();
            ctx.FunctionTable = functionTableLookup; // @@@ 
            ctx.Bridge = services.GetFunctionCompleteLogger(); // aggregates stats instantly. 

            // kernel.Bind<FunctionExecutionContext>().ToConstant(ctx);

            kernel.Bind<IFunctionTableLookup>().ToConstant(functionTableLookup);
            kernel.Bind<IQueueFunction>().ToConstant(new AntaresQueueFunction(qi, ctx));
            // @@@ How to make sure we don't get this from Services() and pull wrong implementation?

            // Spin up background listener
            kernel.Bind<Worker>().ToSelf();

            Worker w = kernel.Get<Worker>();
            Thread t = new Thread( _ =>
                {
                    w.Run();
                });
            t.Start();

            return true;
        }    

        private static void LogRole(TextWriter output)
        {
            output.WriteLine("Antares {0}", Process.GetCurrentProcess().Id);
        }

        internal static IEnumerable<Assembly> GetUserAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies();
        }
    }

    // In-memory executor. 
    class AntaresQueueFunction : QueueFunctionBase
    {
        private readonly FunctionExecutionContext _ctx;
        public AntaresQueueFunction(QueueInterfaces interfaces, FunctionExecutionContext ctx)
            : base(interfaces)
        {
            _ctx = ctx;
        }
        protected override void Work(ExecutionInstanceLogEntity logItem)
        {
            var request = logItem.FunctionInstance;
            var loc = request.Location;

            

            Func<TextWriter, FunctionExecutionResult> fpInvokeFunc =
                (consoleOutput) =>
                {
                    // @@@ May need to be in a new appdomain. 
                    var oldOutput = Console.Out;
                    Console.SetOut(consoleOutput);

                    // @@@ May need to override config to set ICall
                    var result = RunnerHost.Program.MainWorker(request);
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

        public FunctionStore(string accountConnectionString)
        {
            _store = new IndexInMemory();
            var indexer = new Indexer(_store);

            foreach (Assembly a in SimpleBatchStuff.GetUserAssemblies())
            {
                indexer.IndexAssembly(m => OnApplyLocationInfo(accountConnectionString, m), a);
            }
        }        

        FunctionLocation OnApplyLocationInfo(string accountConnectionString, MethodInfo method)
        {
            var loc = new MethodInfoFunctionLocation(method)
            {
                AccountConnectionString = accountConnectionString,
            };
                       
            // ###
            // Apply the username so that multiple different users have unique function ids on a shared dashboard. 
            string userName = Environment.GetEnvironmentVariable("USERNAME") ?? "_";

            loc.Id = Utility.GetAccountName(accountConnectionString) + "." + userName + "." + loc.Id;
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