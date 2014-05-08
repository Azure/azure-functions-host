﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AzureTables;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.Azure.Jobs.Internals;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    // Create host services that point to a logging account. 
    // This will scan for all functions in-memory, publish them to the function dashboard, 
    // and return a set of services that the host can use for invoking, listening, etc. 
    internal class JobHostContext
    {
        private readonly IExecuteFunction _executeFunction;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IFunctionUpdatedLogger _functionUpdatedLogger;
        private readonly Guid _hostInstanceId;
        private readonly Guid _hostId;
        private readonly IProcessTerminationSignalReader _terminationSignalReader;
        private readonly IRunningHostTableWriter _heartbeatTable;

        public JobHostContext(string dataConnectionString, string runtimeConnectionString, string serviceBusDataConnectionString, ITypeLocator typeLocator)
        {
            _hostInstanceId = Guid.NewGuid();
            IConfiguration config = RunnerProgram.InitBinders();

            IFunctionTableLookup functionTableLookup;

            var types = typeLocator.GetTypes().ToArray();
            AddCustomerBinders(config, types);
            functionTableLookup = new FunctionStore(dataConnectionString, serviceBusDataConnectionString, config, types);


            // Determine the host name from the function list
            FunctionDefinition[] functions = functionTableLookup.ReadAll();

            ExecuteFunctionInterfaces interfaces;
            FunctionExecutionContext ctx;

            if (runtimeConnectionString != null)
            {
                // Create logging against a live azure account 

                CloudStorageAccount account = CloudStorageAccount.Parse(runtimeConnectionString);
                IHostTable hostTable = new HostTable(new SdkCloudStorageAccount(account).CreateCloudTableClient());
                string hostName = GetHostName(functions);
                _hostId = hostTable.GetOrCreateHostId(hostName);
                SetHostId(_hostId, functions);

                IPersistentQueue<PersistentQueueMessage> persistentQueue = new PersistentQueue<PersistentQueueMessage>(account);

                // Publish this to Azure logging account so that a web dashboard can see it. 
                PublishFunctionTable(functionTableLookup, dataConnectionString, runtimeConnectionString,
                    persistentQueue);

                var services = GetServices(runtimeConnectionString);

                // Queue interfaces            
                interfaces = services.GetExecuteFunctionInterfaces(); // All for logging. 

                var logger = new WebExecutionLogger(_hostInstanceId, services, LogRole);
                ctx = logger.GetExecutionContext();
                ctx.FunctionsInJobIndexer = services.GetFunctionInJobIndexer();
                ctx.FunctionInstanceLogger = new PersistentQueueFunctionInstanceLogger(persistentQueue);

                _functionInstanceLookup = services.GetFunctionInstanceLookup();
                _functionUpdatedLogger = services.GetFunctionUpdatedLogger();
                _terminationSignalReader = new ProcessTerminationSignalReader(services.Account);
                _heartbeatTable = services.GetRunningHostTableWriter();
            }
            else
            {
                // No auxillary logging. Logging interfaces are nops or in-memory.

                ctx = new FunctionExecutionContext
                {
                    OutputLogDispenser = new ConsoleFunctionOuputLogDispenser(),
                    FunctionsInJobIndexer = new NullFunctionsInJobIndexer()
                };

                interfaces = CreateInMemoryQueueInterfaces();
                _terminationSignalReader = new NullProcessTerminationSignalReader();
                _heartbeatTable = new NullRunningHostTableWriter();
            }

            ctx.Logger = interfaces.Logger;

            // This is direct execution, doesn't queue up. 
            _executeFunction = new WebSitesExecuteFunction(interfaces, config, ctx, new ConsoleHostLogger());
            _functionTableLookup = functionTableLookup;
        }

        // Factory for creating interface implementations that are all in-memory and don't need an 
        // azure storage account.
        private static ExecuteFunctionInterfaces CreateInMemoryQueueInterfaces()
        {
            IFunctionInstanceLookup lookup;
            IFunctionUpdatedLogger functionUpdate;
            ICausalityLogger causalityLogger;

            {
                var x = new LocalFunctionLogger();
                functionUpdate = x;
                lookup = x;
            }

            {
                IAzureTable<TriggerReasonEntity> table = AzureTable<TriggerReasonEntity>.NewInMemory();
                var x = new CausalityLogger(table, lookup);
                causalityLogger = x;
            }

            var interfaces = new ExecuteFunctionInterfaces
            {
                AccountInfo = new AccountInfo(), // For webdashboard. NA in local case
                Logger = functionUpdate,
                Lookup = lookup,
                CausalityLogger = causalityLogger
            };
            return interfaces;
        }

        public IExecuteFunction ExecuteFunction
        {
            get { return _executeFunction; }
        }

        public IFunctionInstanceLookup FunctionInstanceLookup
        {
            get { return _functionInstanceLookup; }
        }

        public IFunctionTableLookup FunctionTableLookup
        {
            get { return _functionTableLookup; }
        }

        public IFunctionUpdatedLogger FunctionUpdatedLogger
        {
            get { return _functionUpdatedLogger; }
        }

        public Guid HostId
        {
            get { return _hostId; }
        }

        public Guid HostInstanceId
        {
            get { return _hostInstanceId; }
        }

        public IRunningHostTableWriter RunningHostTableWriter
        {
            get { return _heartbeatTable; }
        }

        public IProcessTerminationSignalReader TerminationSignalReader
        {
            get { return _terminationSignalReader; }
        }

        // Search for any types that implement ICloudBlobStreamBinder<T>
        // When found, automatically add them as binders to our config. 
        internal static void AddCustomerBinders(IConfiguration config, IEnumerable<Type> types)
        {
            // Scan for any binders
            foreach (var type in types)
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

        // This is a factory for getting interfaces that are bound against azure storage.
        private static Services GetServices(string runtimeConnectionString)
        {
            if (runtimeConnectionString == null)
            {
                throw new InvalidOperationException("Logging account string must be set");
            }

            AccountInfo accountInfo = new AccountInfo
            {
                AccountConnectionString = runtimeConnectionString
            };

            return new Services(accountInfo);
        }

        // Publish functions to the cloud
        // This lets another site go view them. 
        private void PublishFunctionTable(IFunctionTableLookup functionTableLookup, string dataConnectionString,
            string runtimeConnectionString, IPersistentQueue<PersistentQueueMessage> logger)
        {
            HostStartupMessage message = new HostStartupMessage
            {
                HostInstanceId = _hostInstanceId,
                HostId = _hostId,
                Functions = functionTableLookup.ReadAll()
            };
            logger.Enqueue(message);
        }

        private static void LogRole(TextWriter output)
        {
            output.WriteLine("Local {0}", Process.GetCurrentProcess().Id);
        }

        private static void SetHostId(Guid hostId, FunctionDefinition[] functions)
        {
            Debug.Assert(functions != null);

            foreach (FunctionDefinition function in functions)
            {
                if (function == null)
                {
                    continue;
                }

                function.HostId = hostId;
            }
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
                    Console.WriteLine("  Function had errors. See Azure Jobs dashboard for details. Instance id is {0}", logItem.FunctionInstance.Id);
                    Console.ForegroundColor = oldColor;
                }
            }
        }
    }
}
