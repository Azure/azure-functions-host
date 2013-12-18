using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.WindowsAzure.Jobs.Internals;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // Create host services that point to a logging account. 
    // This will scan for all functions in-memory, publish them to the function dashboard, 
    // and return a set of services that the host can use for invoking, listening, etc. 
    class HostContext
    {
        private readonly string _hostName;

        public IFunctionTableLookup _functionTableLookup;
        public IQueueFunction _queueFunction;
        public IRunningHostTableWriter _heartbeatTable;

        public CloudQueue _executionQueue;

        public HostContext(string userAccountConnectionString, string loggingAccountConnectionString)
        {
            var services = GetServices(loggingAccountConnectionString);

            _executionQueue = services.GetExecutionQueue();

            IConfiguration config = RunnerProgram.InitBinders();
            InitConfig(config);

            // Reflect over assembly, looking for functions 
            var functionTableLookup = new FunctionStore(userAccountConnectionString, GetUserAssemblies(), config);

            // Determine the host name from the function list
            _hostName = GetHostName(functionTableLookup.ReadAll());

            // Publish this to Azure logging account so that a web dashboard can see it. 
            PublishFunctionTable(functionTableLookup, userAccountConnectionString, loggingAccountConnectionString);

            // Queue interfaces            
            QueueInterfaces qi = services.GetQueueInterfaces(); // All for logging. 

            string roleName = "local:" + Process.GetCurrentProcess().Id.ToString();
            var logger = new WebExecutionLogger(services, LogRole, roleName);
            var ctx = logger.GetExecutionContext();
            ctx.FunctionTable = functionTableLookup; 
            ctx.Bridge = services.GetFunctionInstanceLogger(); // aggregates stats instantly. 

            // This is direct execution, doesn't queue up. 
            IQueueFunction queueFunction = new AntaresQueueFunction(qi, config, ctx, LogInvoke);

            this._functionTableLookup = functionTableLookup;
            this._heartbeatTable = services.GetRunningHostTableWriter();
            this._queueFunction = queueFunction;
        }

        public string HostName
        {
            get { return _hostName; }
        }

        // Searhc for any types tha implement ICloudBlobStreamBinder<T>
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
