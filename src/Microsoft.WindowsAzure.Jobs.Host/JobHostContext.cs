using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.WindowsAzure.Jobs.Internals;
using Microsoft.WindowsAzure.StorageClient;
using AzureTables;

namespace Microsoft.WindowsAzure.Jobs
{
    // Create host services that point to a logging account. 
    // This will scan for all functions in-memory, publish them to the function dashboard, 
    // and return a set of services that the host can use for invoking, listening, etc. 
    internal class JobHostContext
    {
        private readonly string _hostName;

        public readonly IFunctionTableLookup _functionTableLookup;
        public readonly IQueueFunction _queueFunction;
        public readonly IRunningHostTableWriter _heartbeatTable;

        public readonly CloudQueue _executionQueue;

        public JobHostContext(string userAccountConnectionString, string loggingAccountConnectionString, JobHostTestHooks hooks)
        {
            IConfiguration config = RunnerProgram.InitBinders();

            IFunctionTableLookup functionTableLookup;
            if (hooks.TypeToIndex == null)
            {
                // Normal path
                InitConfig(config);
                functionTableLookup = new FunctionStore(userAccountConnectionString, config, GetUserAssemblies());
            }
            else
            {
                // Just do a single type. Great for unit testing. 
                functionTableLookup = new FunctionStore(userAccountConnectionString, config, hooks.TypeToIndex);
            }

            // Determine the host name from the function list
            _hostName = GetHostName(functionTableLookup.ReadAll());

            QueueInterfaces qi;
            FunctionExecutionContext ctx;

            if (loggingAccountConnectionString != null)
            {
                // Create logging against a live azure account 

                // Publish this to Azure logging account so that a web dashboard can see it. 
                PublishFunctionTable(functionTableLookup, userAccountConnectionString, loggingAccountConnectionString);

                var services = GetServices(loggingAccountConnectionString);
                _executionQueue = services.GetExecutionQueue();

                // Queue interfaces            
                qi = services.GetQueueInterfaces(); // All for logging. 

                string roleName = "local:" + Process.GetCurrentProcess().Id.ToString();
                var logger = new WebExecutionLogger(services, LogRole, roleName);
                ctx = logger.GetExecutionContext();
                ctx.FunctionTable = functionTableLookup;
                ctx.Bridge = services.GetFunctionInstanceLogger(); // aggregates stats instantly.                                 

                _heartbeatTable = services.GetRunningHostTableWriter();
            }
            else
            {
                // No auxillary logging. Logging interfaces are nops or in-memory.
                _heartbeatTable = new NullRunningHostTableWriter();

                ctx = new FunctionExecutionContext
                {
                    OutputLogDispenser = new ConsoleFunctionOuputLogDispenser()
                };

                qi = CreateInMemoryQueueInterfaces();                
            }

            ctx.FunctionTable = functionTableLookup;
            ctx.Logger = qi.Logger;

            // This is direct execution, doesn't queue up. 
            _queueFunction = new AntaresQueueFunction(qi, config, ctx, new ConsoleHostLogger());
            _functionTableLookup = functionTableLookup;
        }

        // Factory for creating interface implementations that are all in-memory and don't need an 
        // azure storage account.
        private static QueueInterfaces CreateInMemoryQueueInterfaces()
        {
            IPrereqManager prereqManager;
            IFunctionInstanceLookup lookup;
            IFunctionUpdatedLogger functionUpdate;
            ICausalityLogger causalityLogger;
            ICausalityReader causalityReader;

            {
                var x = new LocalFunctionLogger();
                functionUpdate = x;
                lookup = x;
            }

            IAzureTable prereqTable = AzureTable.NewInMemory();
            IAzureTable successorTable = AzureTable.NewInMemory();

            prereqManager = new PrereqManager(prereqTable, successorTable, lookup);

            {
                IAzureTable<TriggerReasonEntity> table = AzureTable<TriggerReasonEntity>.NewInMemory();
                var x = new CausalityLogger(table, lookup);
                causalityLogger = x;
                causalityReader = x;
            }

            var qi = new QueueInterfaces
            {
                AccountInfo = new AccountInfo(), // For webdashboard. NA in local case
                Logger = functionUpdate,
                Lookup = lookup,
                PrereqManager = prereqManager,
                CausalityLogger = causalityLogger
            };
            return qi;
        }

        public string HostName
        {
            get { return _hostName; }
        }

        // Search for any types that implement ICloudBlobStreamBinder<T>
        // When found, automatically add them as binders to our config. 
        internal static void InitConfig(IConfiguration config)
        {
            // Scan for any binders
            foreach (var assembly in GetUserAssemblies())
            {
                // Only look at assemblies that reference SB
                if (!Indexer.DoesAssemblyReferenceAzureJobs(assembly))
                {
                    continue;
                }

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        try
                        {
                            foreach (var ti in type.GetInterfaces())
                            {
                                if (ti.IsGenericType)
                                {
                                    var ti2 = ti.GetGenericTypeDefinition();
                                    if (ti2 == typeof(ICloudBlobStreamBinder<>))
                                    {
                                        var tyArg = ti.GetGenericArguments()[0];
                                        var tyBinder = typeof(SimpleBinderProvider<>).MakeGenericType(tyArg);

                                        var objInner = Activator.CreateInstance(type);
                                        var obj = Activator.CreateInstance(tyBinder, objInner);
                                        var it = (ICloudBlobBinderProvider)obj;

                                        config.BlobBinders.Add(it);
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static string GetHostName(FunctionDefinition[] functions)
        {
            // 1. Try to get the assembly name from the first function definition.
            FunctionDefinition firstFunction = functions.FirstOrDefault();

            if (firstFunction != null)
            {
                string hostName = firstFunction.GetAssemblyFullName();

                if (hostName != null)
                {
                    return hostName;
                }
            }

            // 2. If there are no function definitions, try to use the entry assembly.
            Assembly entryAssembly = Assembly.GetEntryAssembly();

            if (entryAssembly != null)
            {
                return entryAssembly.FullName;
            }

            // 3. If there's no entry assembly either, we don't have anything to use.
            return "Unknown";
        }

        private static IEnumerable<Assembly> GetUserAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies();
        }

        // This is a factory for getting interfaces that are bound against azure storage.
        private static Services GetServices(string loggingAccountConnectionString)
        {
            if (loggingAccountConnectionString == null)
            {
                throw new InvalidOperationException("Logging account string must be set");
            }

            AccountInfo accountInfo = new AccountInfo
            {
                AccountConnectionString = loggingAccountConnectionString,
                WebDashboardUri = "illegal" // have non-null value.  @@@
            };

            return new Services(accountInfo);
        }

        // Publish functions to the cloud
        // This lets another site go view them. 
        private void PublishFunctionTable(IFunctionTableLookup functionTableLookup, string userAccountConnectionString, string loggingAccountConnectionString)
        {
            var services = GetServices(loggingAccountConnectionString);
            var cloudTable = services.GetFunctionTable();

            FunctionDefinition[] funcs = cloudTable.ReadAll();

            string scopePrefix = FunctionStore.GetPrefix(userAccountConnectionString);

            foreach (var func in funcs)
            {
                // ### This isn't right. 
                if (func.Location.GetId().StartsWith(scopePrefix, StringComparison.Ordinal))
                {
                    cloudTable.Delete(func);
                }
            }

            // Publish new
            foreach (var func in functionTableLookup.ReadAll())
            {
                cloudTable.Add(func);
            }
        }

        private static void LogRole(TextWriter output)
        {
            output.WriteLine("Local {0}", Process.GetCurrentProcess().Id);
        }

        private class ConsoleHostLogger : IJobHostLogger
        {
            public void LogFunctionStart(FunctionInvokeRequest request)
            {
                Console.WriteLine("Executing: '{0}' because {1}", request.Location.GetShortName(), request.TriggerReason);
            }

            public void LogFunctionEnd(ExecutionInstanceLogEntity logItem)
            {
                if (logItem.GetStatus() == FunctionInstanceStatus.CompletedFailed)
                {
                    var oldColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  Function had errors. See SimpleBatch dashboard for details. Instance id is {0}", logItem.FunctionInstance.Id);
                    Console.ForegroundColor = oldColor;
                }
            }
        }
    }
}
