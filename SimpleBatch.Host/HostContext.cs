using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch.Internals;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SimpleBatch
{
    // Create host services that point to a logging account. 
    // This will scan for all functions in-memory, publish them to the function dashboard, 
    // and return a set of services that the host can use for invoking, listening, etc. 
    class HostContext
    {
        public IFunctionTableLookup _functionTableLookup;
        public IQueueFunction _queueFunction;

        public CloudQueue _executionQueue;

        public HostContext(string userAccountConnectionString, string loggingAccountConnectionString)
        {
            var services = GetServices(loggingAccountConnectionString);

            _executionQueue = services.GetExecutionQueue();

            IConfiguration config = RunnerHost.Program.InitBinders();
            InitConfig(config);

            // Reflect over assembly, looking for functions 
            var functionTableLookup = new FunctionStore(userAccountConnectionString, GetUserAssemblies(), config);

            // Publish this to Azure logging account so that a web dashboard can see it. 
            PublishFunctionTable(functionTableLookup, userAccountConnectionString, loggingAccountConnectionString);

            // Queue interfaces            
            QueueInterfaces qi = services.GetQueueInterfaces(); // All for logging. 

            string roleName = "local:" + Process.GetCurrentProcess().Id.ToString();
            var logger = new WebExecutionLogger(services, LogRole, roleName);
            var ctx = logger.GetExecutionContext();
            ctx.FunctionTable = functionTableLookup; 
            ctx.Bridge = services.GetFunctionCompleteLogger(); // aggregates stats instantly. 

            // This is direct execution, doesn't queue up. 
            IQueueFunction queueFunction = new AntaresQueueFunction(qi, config, ctx, LogInvoke);

            this._functionTableLookup = functionTableLookup;
            this._queueFunction = queueFunction;
        }

        // Searhc for any types tha implement ICloudBlobStreamBinder<T>
        // When found, automatically add them as binders to our config. 
        static void InitConfig(IConfiguration config)
        {
            // Scan for any binders
            foreach (var assembly in GetUserAssemblies())
            {
                // Only look at assemblies that reference SB
                if (!Orchestrator.Indexer.DoesAssemblyReferenceSimpleBatch(assembly))
                {
                    continue;
                }

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        try
                        {
                            foreach(var ti in type.GetInterfaces())
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

                            //config.BlobBinders.Add(
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

        static IEnumerable<Assembly> GetUserAssemblies()
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
        void PublishFunctionTable(IFunctionTableLookup functionTableLookup, string userAccountConnectionString, string loggingAccountConnectionString)
        {
            var services = GetServices(loggingAccountConnectionString);
            var cloudTable = services.GetFunctionTable();

            FunctionDefinition[] funcs = cloudTable.ReadAll();

            string scopePrefix = FunctionStore.GetPrefix(userAccountConnectionString);

            foreach (var func in funcs)
            {
                // ### This isn't right. 
                if (func.Location.GetId().StartsWith(scopePrefix))
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

        private void LogInvoke(FunctionInvokeRequest request)
        {
            Console.WriteLine("Executing: '{0}' because {1}", request.Location.GetShortName(), request.TriggerReason);
        }

        private static void LogRole(TextWriter output)
        {
            output.WriteLine("Local {0}", Process.GetCurrentProcess().Id);
        }
    }
}