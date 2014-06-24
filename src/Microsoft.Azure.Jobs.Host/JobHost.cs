﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Queues.Listeners;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Defines properties and methods to locate Job methods and listen to trigger events in order
    /// to execute Job methods.
    /// </summary>
    public class JobHost
    {
        // Where we log things to (null if logging is not supported).
        private readonly string _dashboardConnectionString;

        // The user account that we listen on.
        // This is the account that the bindings resolve against.
        private readonly string _storageConnectionString;
        private readonly string _serviceBusConnectionString;

        private readonly JobHostContext _hostContext;

        internal const string DashboardConnectionStringName = "Dashboard";
        internal const string StorageConnectionStringName = "Storage";
        internal const string ServiceBusConnectionStringName = "ServiceBus";

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHost"/> class, using a Microsoft Azure Storage connection
        /// string located in the connectionStrings section of the configuration file or in environment variables.
        /// </summary>
        public JobHost()
            : this(new JobHostConfiguration())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHost"/> class using the configuration provided.
        /// </summary>
        /// <param name="configuration">The job host configuration.</param>
        public JobHost(JobHostConfiguration configuration)
            : this((IServiceProvider)ThrowIfNull(configuration))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHost"/> class using the service provider provided.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        public JobHost(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException("serviceProvider");
            }

            IConnectionStringProvider connectionStringProvider = serviceProvider.GetConnectionStringProvider();
            _storageConnectionString = connectionStringProvider.GetConnectionString(StorageConnectionStringName);
            _serviceBusConnectionString = connectionStringProvider.GetConnectionString(ServiceBusConnectionStringName);
            _dashboardConnectionString = connectionStringProvider.GetConnectionString(DashboardConnectionStringName);

            ValidateConnectionStrings(serviceProvider.GetStorageValidator());

            // This will do heavy operations like indexing. 
            _hostContext = GetHostContext(serviceProvider.GetTypeLocator(), serviceProvider.GetNameResolver());
        }

        private void ValidateConnectionStrings(IStorageValidator storageValidator)
        {
            string storageConnectionStringValidationError;
            if (!storageValidator.TryValidateConnectionString(_storageConnectionString, out storageConnectionStringValidationError))
            {
                var msg = FormatConnectionStringValidationError("storage", StorageConnectionStringName, storageConnectionStringValidationError);
                throw new InvalidOperationException(msg);
            }
            if (_dashboardConnectionString != null)
            {
                if (_dashboardConnectionString != _storageConnectionString)
                {
                    string dashboardConnectionStringValidationError;
                    if (!storageValidator.TryValidateConnectionString(_dashboardConnectionString, out dashboardConnectionStringValidationError))
                    {
                        var msg = FormatConnectionStringValidationError("dashboard", DashboardConnectionStringName, dashboardConnectionStringValidationError);
                        throw new InvalidOperationException(msg);
                    }
                }
            }
        }

        internal static string FormatConnectionStringValidationError(string connectionStringType, string connectionStringName, string validationErrorMessage)
        {
            return String.Format(CultureInfo.CurrentCulture,
                "Failed to validate Microsoft Azure Jobs {0} connection string: {2}" + Environment.NewLine +
                "The Microsoft Azure Jobs connection string is specified by setting a connection string named '{1}' in the connectionStrings section of the .config file, " +
                "or with an environment variable named '{1}', or by using a constructor for JobHostConfiguration that accepts connection strings.",
                connectionStringType, AmbientConnectionStringProvider.GetPrefixedConnectionStringName(connectionStringName), validationErrorMessage);
        }

        private JobHostContext GetHostContext(ITypeLocator typesLocator, INameResolver nameResolver)
        {
            var hostContext = new JobHostContext(_dashboardConnectionString, _storageConnectionString, _serviceBusConnectionString, typesLocator, nameResolver);
            return hostContext;
        }

        /// <summary>
        /// Runs the jobs on a background thread and return immediately.
        /// The trigger listeners and jobs will execute on the background thread.
        /// </summary>
        public void RunOnBackgroundThread()
        {
            RunOnBackgroundThread(CancellationToken.None);
        }

        /// <summary>
        /// Runs the jobs on a background thread and return immediately.
        /// The trigger listeners and jobs will execute on the background thread.
        /// The thread exits when the cancellation token is signalled.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        public void RunOnBackgroundThread(CancellationToken token)
        {
            Thread thread = new Thread(_ => RunAndBlock(token));
            thread.Start();
        }

        /// <summary>
        /// Runs the jobs on the current thread.
        /// The trigger listeners and jobs will execute on the current thread.
        /// </summary>
        public void RunAndBlock()
        {
            RunAndBlock(CancellationToken.None);
        }

        /// <summary>
        /// Runs the jobs on the current thread.
        /// The trigger listeners and jobs will execute on the current thread.
        /// The thread will be blocked until the cancellation token is signalled.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        public void RunAndBlock(CancellationToken token)
        {
            RunAndBlock(token, () =>
            {
                Thread.Sleep(2 * 1000);
                Console.Write(".");
            });
        }

        // Run the jobs on the current thread. 
        // Execute as much work as possible, and then invoke pauseAction() when there's a pause in the work. 
        internal void RunAndBlock(CancellationToken token, Action pauseAction)
        {
            using (WebJobsShutdownWatcher watcher = new WebJobsShutdownWatcher())
            using (IntervalSeparationTimer timer = CreateHeartbeatTimer(hostIsRunning: true))
            {
                token = CancellationTokenSource.CreateLinkedTokenSource(token, watcher.Token).Token;
                timer.Start(executeFirst: true);

                try
                {
                    NotifyNewBlobViaInMemory fastpathNotify = new NotifyNewBlobViaInMemory();
                    CloudStorageAccount account = CloudStorageAccount.Parse(_storageConnectionString);

                    RuntimeBindingProviderContext context = new RuntimeBindingProviderContext
                    {
                        BindingProvider = _hostContext.BindingProvider,
                        NotifyNewBlob = fastpathNotify,
                        CancellationToken = token,
                        NameResolver = _hostContext.NameResolver,
                        StorageAccount = account,
                        ServiceBusConnectionString = _serviceBusConnectionString
                    };

                    CloudQueueClient queueClient = account.CreateCloudQueueClient();
                    IListener sharedQueueListener;
                    IListener instanceQueueListener;

                    if (_dashboardConnectionString != null)
                    {
                        sharedQueueListener = HostMessageListener.Create(
                            queueClient.GetQueueReference(_hostContext.SharedQueueName),
                            _hostContext.ExecuteFunction,
                            _hostContext.FunctionTableLookup,
                            _hostContext.FunctionInstanceLogger,
                            context);
                        instanceQueueListener = HostMessageListener.Create(
                            queueClient.GetQueueReference(_hostContext.InstanceQueueName),
                            _hostContext.ExecuteFunction,
                            _hostContext.FunctionTableLookup,
                            _hostContext.FunctionInstanceLogger,
                            context);
                    }
                    else
                    {
                        sharedQueueListener = null;
                        instanceQueueListener = null;
                    }

                    IListener listener = CreateListener(_hostContext.ExecuteFunction, context,
                        _hostContext.FunctionTableLookup.ReadAll(), sharedQueueListener, instanceQueueListener);

                    Credentials credentials = new Credentials
                    {
                        StorageConnectionString = _storageConnectionString,
                        ServiceBusConnectionString = _serviceBusConnectionString
                    };

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    listener.Start();

                    Worker worker = new Worker(_hostContext.FunctionTableLookup, _hostContext.ExecuteFunction,
                        _hostContext.FunctionInstanceLogger, fastpathNotify, fastpathNotify, credentials);

                    worker.StartPolling(context);

                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            worker.Poll(context);

                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            pauseAction();
                        }
                    }
                    finally
                    {
                        listener.Stop();
                    }
                }
                finally
                {
                    timer.Stop();
                }
            }
        }

        /// <summary>Invokes a job function.</summary>
        /// <param name="method">A MethodInfo representing the job method to execute.</param>
        public void Call(MethodInfo method)
        {
            Call(method, arguments: (IDictionary<string, object>)null);
        }

        /// <summary>Invokes a job function.</summary>
        /// <param name="method">A MethodInfo representing the job method to execute.</param>
        /// <param name="arguments">
        /// An object with public properties representing argument names and values to bind to parameters in the job
        /// method.
        /// </param>
        public void Call(MethodInfo method, object arguments)
        {
            Call(method, arguments, CancellationToken.None);
        }

        /// <summary>Invokes a job function.</summary>
        /// <param name="method">A MethodInfo representing the job method to execute.</param>
        /// <param name="arguments">The argument names and values to bind to parameters in the job method.</param>
        public void Call(MethodInfo method, IDictionary<string, object> arguments)
        {
            Call(method, arguments, CancellationToken.None);
        }

        /// <summary>Invokes a job function.</summary>
        /// <param name="method">A MethodInfo representing the job method to execute.</param>
        /// <param name="arguments">An object with public properties representing argument names and values to bind to the parameter tokens in the job method's arguments.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        public void Call(MethodInfo method, object arguments, CancellationToken cancellationToken)
        {
            IDictionary<string, object> argumentsDictionary = ObjectDictionaryConverter.AsDictionary(arguments);
            Call(method, argumentsDictionary, cancellationToken);
        }

        /// <summary>Invokes a job function.</summary>
        /// <param name="method">A MethodInfo representing the job method to execute.</param>
        /// <param name="arguments">The argument names and values to bind to parameters in the job method.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        public void Call(MethodInfo method, IDictionary<string, object> arguments, CancellationToken cancellationToken)
        {
            if (method == null)
            {
                throw new ArgumentNullException("method");
            }

            FunctionDefinition func = ResolveFunctionDefinition(method, _hostContext.FunctionTableLookup);
            RuntimeBindingProviderContext context = new RuntimeBindingProviderContext
            {
                BindingProvider = _hostContext.BindingProvider,
                CancellationToken = cancellationToken,
                NameResolver = _hostContext.NameResolver,
                StorageAccount = _storageConnectionString != null ? CloudStorageAccount.Parse(_storageConnectionString) : null,
                ServiceBusConnectionString = _serviceBusConnectionString
            };
            IFunctionInstance instance = CreateFunctionInstance(func, arguments, context);

            FunctionInvocationResult result;

            using (WebJobsShutdownWatcher watcher = new WebJobsShutdownWatcher())
            using (IntervalSeparationTimer timer = CreateHeartbeatTimer(hostIsRunning: false))
            {
                cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, watcher.Token).Token;
                timer.Start(executeFirst: true);

                try
                {
                    result = _hostContext.ExecuteFunction.Execute(instance, context);
                }
                finally
                {
                    timer.Stop();
                }
            }

            VerifySuccess(result);
        }

        private static IFunctionInstance CreateFunctionInstance(FunctionDefinition func,
            IDictionary<string, object> parameters, RuntimeBindingProviderContext context)
        {
            Guid functionInstanceId = Guid.NewGuid();

            return new FunctionInstance(functionInstanceId, null, ExecutionReason.HostCall,
                new InvokeBindCommand(functionInstanceId, func, parameters, context), func.Descriptor, func.Method);
        }

        private IntervalSeparationTimer CreateHeartbeatTimer(bool hostIsRunning)
        {
            ICanFailCommand heartbeat = CreateHeartbeat(hostIsRunning);

            if (heartbeat == null)
            {
                return new IntervalSeparationTimer(new NullTimerCommand());
            }

            return LinearSpeedupTimerCommand.CreateTimer(heartbeat,
                HeartbeatIntervals.NormalSignalInterval, HeartbeatIntervals.MinimumSignalInterval);
        }

        private ICanFailCommand CreateHeartbeat(bool hostIsRunning)
        {
            if (!hostIsRunning)
            {
                return null;
            }
            else
            {
                return CreateUpdateHostHeartbeatCommand();
            }
        }

        private static IListener CreateListener(IExecuteFunction executeFunction, RuntimeBindingProviderContext context,
            IEnumerable<FunctionDefinition> functionDefinitions, IListener sharedQueueListener,
            IListener instanceQueueListener)
        {
            IFunctionExecutor executor = new ExecuteFunctionExecutor(executeFunction, context);

            List<IListener> listeners = new List<IListener>();

            foreach (FunctionDefinition functionDefinition in functionDefinitions)
            {
                IListenerFactory listenerFactory = functionDefinition.ListenerFactory;

                if (listenerFactory == null)
                {
                    continue;
                }

                IListener listener = listenerFactory.Create(executor, context);
                listeners.Add(listener);
            }

            if (sharedQueueListener != null)
            {
                listeners.Add(sharedQueueListener);
            }

            if (instanceQueueListener != null)
            {
                listeners.Add(instanceQueueListener);
            }

            return new CompositeListener(listeners);
        }

        private UpdateHostHeartbeatCommand CreateUpdateHostHeartbeatCommand()
        {
            return new UpdateHostHeartbeatCommand(_hostContext.HeartbeatCommand);
        }

        // Throw if the function failed. 
        private static void VerifySuccess(FunctionInvocationResult result)
        {
            if (!result.Succeeded)
            {
                result.ExceptionInfo.Throw();
            }
        }

        private static JobHostConfiguration ThrowIfNull(JobHostConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            return configuration;
        }

        private FunctionDefinition ResolveFunctionDefinition(MethodInfo method, IFunctionTableLookup functionTableLookup)
        {
            foreach (FunctionDefinition func in functionTableLookup.ReadAll())
            {
                if (func.Method.Equals(method))
                {
                    return func;
                }
            }

            string msg = String.Format("'{0}' can't be invoked from Azure Jobs. Is it missing Azure Jobs bindings?", method);
            throw new InvalidOperationException(msg);
        }
    }
}
