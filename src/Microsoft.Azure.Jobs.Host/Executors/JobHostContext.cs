﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    // Create host services that point to a logging account. 
    // This will scan for all functions in-memory, publish them to the function dashboard, 
    // and return a set of services that the host can use for invoking, listening, etc. 
    internal class JobHostContext
    {
        private static readonly Func<string, ConnectionStringDescriptor> _serviceBusConnectionStringDescriptorFactory =
            CreateServiceBusConnectionStringDescriptorFactory();

        private readonly INameResolver _nameResolver;
        private readonly IExecuteFunction _executeFunction;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly FunctionIndex _functionIndex;
        private readonly string _sharedQueueName;
        private readonly string _instanceQueueName;
        private readonly HostOutputMessage _hostOutputMessage;
        private readonly IHeartbeatCommand _heartbeatCommand;

        public JobHostContext(CloudStorageAccount dashboardAccount, CloudStorageAccount storageAccount, string serviceBusConnectionString, ITypeLocator typeLocator, INameResolver nameResolver)
        {
            _nameResolver = nameResolver;
            Guid id = Guid.NewGuid();

            _functionIndex = FunctionIndex.Create(new FunctionIndexContext(typeLocator, nameResolver, storageAccount,
                serviceBusConnectionString));

            FunctionExecutionContext ctx;

            if (dashboardAccount != null)
            {
                // Create logging against a live azure account 

                CloudBlobClient blobClient = dashboardAccount.CreateCloudBlobClient();
                IHostIdManager hostIdManager = new HostIdManager(blobClient);
                // Determine the host name from the method list
                Assembly hostAssembly = GetHostAssembly(_functionIndex.ReadAllMethods());

                string hostName = hostAssembly != null ? hostAssembly.FullName : "Unknown";
                string sharedHostName = dashboardAccount.Credentials.AccountName + "/" + hostName;
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
                PublishFunctionTable(_functionIndex, persistentQueueWriter);

                _functionInstanceLogger = new CompositeFunctionInstanceLogger(
                    new PersistentQueueFunctionInstanceLogger(persistentQueueWriter),
                    new ConsoleFunctionInstanceLogger());
                ctx = new FunctionExecutionContext
                {
                    HostOutputMessage = _hostOutputMessage,
                    OutputLogFactory = new BlobFunctionOutputLogger(blobClient),
                    FunctionInstanceLogger = _functionInstanceLogger
                };

                _heartbeatCommand = new HeartbeatCommand(dashboardAccount, heartbeatDescriptor.SharedContainerName,
                    heartbeatDescriptor.SharedDirectoryName + "/" + heartbeatDescriptor.InstanceBlobName);
            }
            else
            {
                // No auxillary logging. Logging interfaces are nops or in-memory.

                ctx = new FunctionExecutionContext
                {
                    HostOutputMessage = new DataOnlyHostOutputMessage(),
                    OutputLogFactory = new ConsoleFunctionOuputLogFactory(),
                    FunctionInstanceLogger = new ConsoleFunctionInstanceLogger()
                };

                _heartbeatCommand = new NullHeartbeatCommand();
            }

            // This is direct execution, doesn't queue up. 
            _executeFunction = new WebSitesExecuteFunction(ctx);
        }

        public IExecuteFunction ExecuteFunction
        {
            get { return _executeFunction; }
        }

        public IFunctionInstanceLogger FunctionInstanceLogger
        {
            get { return _functionInstanceLogger; }
        }

        public IFunctionIndexLookup FunctionLookup
        {
            get { return _functionIndex; }
        }

        public IFunctionIndex Functions
        {
            get { return _functionIndex; }
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
            get { return _functionIndex.BindingProvider; }
        }

        public INameResolver NameResolver
        {
            get { return _nameResolver; }
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

        private static Assembly GetHostAssembly(IEnumerable<MethodInfo> methods)
        {
            // 1. Try to get the assembly name from the first method.
            MethodInfo firstMethod = methods.FirstOrDefault();

            if (firstMethod != null)
            {
                return firstMethod.DeclaringType.Assembly;
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
        private void PublishFunctionTable(IFunctionIndex functionIndex, IPersistentQueueWriter<PersistentQueueMessage> logger)
        {
            IEnumerable<FunctionDescriptor> functions = functionIndex.ReadAllDescriptors();

            HostStartedMessage message = new HostStartedMessage
            {
                HostInstanceId = _hostOutputMessage.HostInstanceId,
                HostDisplayName = _hostOutputMessage.HostDisplayName,
                SharedQueueName = _hostOutputMessage.SharedQueueName,
                InstanceQueueName = _hostOutputMessage.InstanceQueueName,
                Heartbeat = _hostOutputMessage.Heartbeat,
                Credentials = _hostOutputMessage.Credentials,
                WebJobRunIdentifier = _hostOutputMessage.WebJobRunIdentifier,
                Functions = functions
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
