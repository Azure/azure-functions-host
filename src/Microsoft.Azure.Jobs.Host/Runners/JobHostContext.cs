﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // Create host services that point to a logging account. 
    // This will scan for all functions in-memory, publish them to the function dashboard, 
    // and return a set of services that the host can use for invoking, listening, etc. 
    internal class JobHostContext
    {
        private static readonly Func<string, ConnectionStringDescriptor> _serviceBusConnectionStringDescriptorFactory =
            CreateServiceBusConnectionStringDescriptorFactory();

        private readonly IExecuteFunction _executeFunction;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly string _sharedQueueName;
        private readonly string _instanceQueueName;
        private readonly HostOutputMessage _hostOutputMessage;
        private readonly IHeartbeatCommand _heartbeatCommand;
        private readonly FunctionStore _functionStore;

        public JobHostContext(string dashboardConnectionString, string storageConnectionString, string serviceBusConnectionString, ITypeLocator typeLocator, INameResolver nameResolver)
        {
            Guid id = Guid.NewGuid();
            IConfiguration config = new Configuration();
            config.NameResolver = nameResolver;

            IFunctionTableLookup functionTableLookup;

            var types = typeLocator.GetTypes().ToArray();
            AddCustomerBinders(config, types);

            CloudStorageAccount storageAccount;

            if (storageConnectionString == null)
            {
                storageAccount = null;
            }
            else
            {
                storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            }

            _functionStore = new FunctionStore(storageAccount, serviceBusConnectionString, config, types);
            functionTableLookup = _functionStore;

            // Determine the host name from the function list
            FunctionDefinition[] functions = functionTableLookup.ReadAll();

            FunctionExecutionContext ctx;

            if (dashboardConnectionString != null)
            {
                // Create logging against a live azure account 

                CloudStorageAccount account = CloudStorageAccount.Parse(dashboardConnectionString);
                CloudBlobClient blobClient = account.CreateCloudBlobClient();
                ICloudTableClient tableClient = new SdkCloudStorageAccount(account).CreateCloudTableClient();
                IHostIdManager hostIdManager = new HostIdManager(blobClient);
                Assembly hostAssembly = GetHostAssembly(functions);

                string hostName = hostAssembly != null ? hostAssembly.FullName : "Unknown";
                string sharedHostName = account.Credentials.AccountName + "/" + hostName;
                Guid hostId = hostIdManager.GetOrCreateHostId(sharedHostName);
                _sharedQueueName = HostQueueNames.GetHostQueueName(hostId);
                _instanceQueueName = HostQueueNames.GetHostQueueName(id);
                string displayName = hostAssembly != null ? hostAssembly.GetName().Name : "Unknown";

                HeartbeatDescriptor heartbeatDescriptor = new HeartbeatDescriptor
                {
                    SharedContainerName = HostContainerNames.Hosts,
                    SharedDirectoryName = HostDirectoryNames.Heartbeats + "/" + hostId.ToString("N"),
                    InstanceBlobName = id.ToString("N"),
                    ExpirationInSeconds = (int)HeartbeatIntervals.ExpirationInterval.TotalSeconds
                };

                CredentialsDescriptor credentialsDescriptor = CreateCredentialsDescriptor(storageAccount,
                    serviceBusConnectionString);

                _hostOutputMessage = new DataOnlyHostOutputMessage {
                    HostInstanceId = id,
                    HostDisplayName = displayName,
                    SharedQueueName = _sharedQueueName,
                    InstanceQueueName = _instanceQueueName,
                    Heartbeat = heartbeatDescriptor,
                    Credentials = credentialsDescriptor,
                    WebJobRunIdentifier = WebJobRunIdentifier.Current,
                };

                IPersistentQueueWriter<PersistentQueueMessage> persistentQueueWriter =
                    new PersistentQueueWriter<PersistentQueueMessage>(blobClient);

                // Publish this to Azure logging account so that a web dashboard can see it. 
                PublishFunctionTable(functionTableLookup, storageConnectionString, serviceBusConnectionString,
                    persistentQueueWriter);

                var logger = new WebExecutionLogger(blobClient, _hostOutputMessage);
                ctx = logger.GetExecutionContext();
                _functionInstanceLogger = new CompositeFunctionInstanceLogger(
                    new PersistentQueueFunctionInstanceLogger(persistentQueueWriter),
                    new ConsoleFunctionInstanceLogger());
                ctx.FunctionInstanceLogger = _functionInstanceLogger;

                _heartbeatCommand = new HeartbeatCommand(account, heartbeatDescriptor.SharedContainerName,
                    heartbeatDescriptor.SharedDirectoryName + "/" + heartbeatDescriptor.InstanceBlobName);
            }
            else
            {
                // No auxillary logging. Logging interfaces are nops or in-memory.

                ctx = new FunctionExecutionContext
                {
                    HostOutputMessage = new DataOnlyHostOutputMessage(),
                    OutputLogDispenser = new ConsoleFunctionOuputLogDispenser(),
                    FunctionInstanceLogger = new ConsoleFunctionInstanceLogger()
                };

                _heartbeatCommand = new NullHeartbeatCommand();
            }

            // This is direct execution, doesn't queue up. 
            _executeFunction = new WebSitesExecuteFunction(ctx);
            _functionTableLookup = functionTableLookup;
        }

        public IExecuteFunction ExecuteFunction
        {
            get { return _executeFunction; }
        }

        public IFunctionInstanceLogger FunctionInstanceLogger
        {
            get { return _functionInstanceLogger; }
        }

        public IFunctionTableLookup FunctionTableLookup
        {
            get { return _functionTableLookup; }
        }

        public string SharedQueueName
        {
            get { return _sharedQueueName; }
        }

        public string InstanceQueueName
        {
            get { return _instanceQueueName; }
        }

        public IHeartbeatCommand HeartbeatCommand
        {
            get { return _heartbeatCommand; }
        }

        public IBindingProvider BindingProvider
        {
            get { return _functionStore.BindingProvider; }
        }

        public INameResolver NameResolver
        {
            get { return _functionStore.NameResolver; }
        }

        private static Func<string, ConnectionStringDescriptor> CreateServiceBusConnectionStringDescriptorFactory()
        {
            Type factoryType = ServiceBusExtensionTypeLoader.Get(
                "Microsoft.Azure.Jobs.ServiceBus.Listeners.ServiceBusConnectionStringDescriptorFactory");

            if (factoryType == null)
            {
                return (_) => null;
            }

            MethodInfo method = factoryType.GetMethod("Create", new Type[] { typeof(string) });
            return (Func<string, ConnectionStringDescriptor>)Delegate.CreateDelegate(
                typeof(Func<string, ConnectionStringDescriptor>), method);
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
                                config.CloudBlobStreamBinderTypes.Add(type);
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static Assembly GetHostAssembly(FunctionDefinition[] functions)
        {
            // 1. Try to get the assembly name from the first function definition.
            FunctionDefinition firstFunction = functions.FirstOrDefault();

            if (firstFunction != null)
            {
                return firstFunction.Method.DeclaringType.Assembly;
            }

            // 2. If there are no function definitions, try to use the entry assembly.
            Assembly entryAssembly = Assembly.GetEntryAssembly();

            if (entryAssembly != null)
            {
                return entryAssembly;
            }

            // 3. If there's no entry assembly either, we don't have anything to use.
            return null;
        }

        // Publish functions to the cloud
        // This lets another site go view them. 
        private void PublishFunctionTable(IFunctionTableLookup functionTableLookup, string storageConnectionString,
            string serviceBusConnectionString, IPersistentQueueWriter<PersistentQueueMessage> logger)
        {
            FunctionDefinition[] functions = functionTableLookup.ReadAll();

            FunctionDescriptor[] functionDescriptors = new FunctionDescriptor[functions.Length];

            for (int index = 0; index < functions.Length; index++)
            {
                functionDescriptors[index] = functions[index].ToFunctionDescriptor();
            }

            HostStartedMessage message = new HostStartedMessage
            {
                HostInstanceId = _hostOutputMessage.HostInstanceId,
                HostDisplayName = _hostOutputMessage.HostDisplayName,
                SharedQueueName = _hostOutputMessage.SharedQueueName,
                InstanceQueueName = _hostOutputMessage.InstanceQueueName,
                Heartbeat = _hostOutputMessage.Heartbeat,
                Credentials = _hostOutputMessage.Credentials,
                WebJobRunIdentifier = _hostOutputMessage.WebJobRunIdentifier,
                Functions = functionDescriptors
            };

            logger.Enqueue(message);
        }

        private static CredentialsDescriptor CreateCredentialsDescriptor(CloudStorageAccount storageAccount,
            string serviceBusConnectionString)
        {
            List<ConnectionStringDescriptor> connectionStrings = new List<ConnectionStringDescriptor>();

            if (storageAccount != null)
            {
                connectionStrings.Add(new StorageConnectionStringDescriptor
                {
                    Account = storageAccount.Credentials.AccountName,
                    ConnectionString = storageAccount.ToString(exportSecrets: true)
                });
            }

            if (serviceBusConnectionString != null)
            {
                ConnectionStringDescriptor serviceBusConnectionStringDescriptor =
                    _serviceBusConnectionStringDescriptorFactory.Invoke(serviceBusConnectionString);

                if (serviceBusConnectionStringDescriptor != null)
                {
                    connectionStrings.Add(serviceBusConnectionStringDescriptor);
                }
            }

            if (connectionStrings.Count == 0)
            {
                return null;
            }

            return new CredentialsDescriptor
            {
                ConnectionStrings = connectionStrings.ToArray()
            };
        }

        private class DataOnlyHostOutputMessage : HostOutputMessage
        {
            internal override void AddMetadata(IDictionary<string, string> metadata)
            {
                throw new NotSupportedException();
            }
        }
    }
}
