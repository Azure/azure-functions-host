﻿using System;
using System.Collections.Generic;
﻿using System.Globalization;
﻿using System.Reflection;
using System.Threading;
﻿using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Defines properties and methods to locate Job methods and listen to trigger events in order
    /// to execute Job methods.
    /// </summary>
    public class JobHost
    {
        // Where we log things to. 
        // Null if logging is not supported (this is required for pumping).        
        private readonly string _runtimeConnectionString;

        // The user account that we listen on.
        // This is the account that the bindings resolve against.
        private readonly string _dataConnectionString;
        private readonly string _serviceBusDataConnectionString;

        private readonly JobHostContext _hostContext;

        internal const string LoggingConnectionStringName = "AzureJobsRuntime";
        internal const string DataConnectionStringName = "AzureJobsData";
        internal const string ServiceBusConnectionStringName = "AzureJobsServiceBusData";

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
            _dataConnectionString = connectionStringProvider.GetConnectionString(DataConnectionStringName);
            _serviceBusDataConnectionString = connectionStringProvider.GetConnectionString(ServiceBusConnectionStringName);
            _runtimeConnectionString = connectionStringProvider.GetConnectionString(LoggingConnectionStringName);

            ValidateConnectionStrings(serviceProvider.GetStorageValidator());

            // This will do heavy operations like indexing. 
            _hostContext = GetHostContext(serviceProvider.GetTypeLocator());
        }

        private void ValidateConnectionStrings(IStorageValidator storageValidator)
        {
            string dataConnectionStringValidationError;
            if (!storageValidator.TryValidateConnectionString(_dataConnectionString, out dataConnectionStringValidationError))
            {
                var msg = FormatConnectionStringValidationError("data", DataConnectionStringName, dataConnectionStringValidationError);
                throw new InvalidOperationException(msg);
            }
            if (_runtimeConnectionString != null)
            {
                if (_runtimeConnectionString != _dataConnectionString)
                {
                    string runtimeConnectionStringValidationError;
                    if (!storageValidator.TryValidateConnectionString(_runtimeConnectionString, out runtimeConnectionStringValidationError))
                    {
                        var msg = FormatConnectionStringValidationError("runtime", LoggingConnectionStringName, runtimeConnectionStringValidationError);
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
                connectionStringType, connectionStringName, validationErrorMessage);
        }

        /// <summary>
        /// Gets the storage account name from the connection string.
        /// </summary>
        public string UserAccountName
        {
            get { return Utility.GetAccountName(_dataConnectionString); }
        }

        private JobHostContext GetHostContext(ITypeLocator typesLocator)
        {
            var hostContext = new JobHostContext(_dataConnectionString, _runtimeConnectionString, _serviceBusDataConnectionString, typesLocator);
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
            using (IntervalSeparationTimer timer = CreateHeartbeatTimer(hostIsRunning: true))
            {
                timer.Start(executeFirst: true);

                try
                {
                    INotifyNewBlobListener fastpathNotify = new NotifyNewBlobViaInMemory();
                    QueueTrigger invokeTrigger;

                    if (_runtimeConnectionString != null)
                    {
                        invokeTrigger = new QueueTrigger
                        {
                            QueueName = QueueNames.GetHostQueueName(_hostContext.HostId),
                            AccountConnectionString = _runtimeConnectionString
                        };
                    }
                    else
                    {
                        invokeTrigger = null;
                    }

                    Worker worker = new Worker(invokeTrigger, _hostContext.FunctionTableLookup, _hostContext.ExecuteFunction,
                        _hostContext.FunctionInstanceLogger, fastpathNotify);

                    while (!token.IsCancellationRequested)
                    {
                        worker.Poll(token);

                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        pauseAction();
                    }
                }
                finally
                {
                    timer.Stop();
                }
            }
        }

        /// <summary>
        /// Invoke a specific function specified by the method parameter.
        /// </summary>
        /// <param name="method">A MethodInfo representing the job method to execute.</param>
        public void Call(MethodInfo method)
        {
            Call(method, arguments: null);
        }

        /// <summary>
        /// Invoke a specific function specified by the method parameter.
        /// </summary>
        /// <param name="method">A MethodInfo representing the job method to execute.</param>
        /// <param name="arguments">An object with public properties representing argument names and values to bind to the parameter tokens in the job method's arguments.</param>
        public void Call(MethodInfo method, object arguments)
        {
            Call(method, arguments, CancellationToken.None);
        }

        /// <summary>
        /// Invoke a specific function specified by the method parameter.
        /// </summary>
        /// <param name="method">A MethodInfo representing the job method to execute.</param>
        /// <param name="arguments">An object with public properties representing argument names and values to bind to the parameter tokens in the job method's arguments.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        public void Call(MethodInfo method, object arguments, CancellationToken cancellationToken)
        {
            if (method == null)
            {
                throw new ArgumentNullException("method");
            }

            IDictionary<string, string> args2 = ObjectBinderHelpers.ConvertObjectToDict(arguments);

            FunctionDefinition func = ResolveFunctionDefinition(method, _hostContext.FunctionTableLookup);
            FunctionInvokeRequest instance = Worker.GetFunctionInvocation(func, args2);

            instance.TriggerReason = new InvokeTriggerReason
            {
                Message = String.Format("This was function was programmatically called via the host APIs.")
            };

            ExecutionInstanceLogEntity logItem;

            using (IntervalSeparationTimer timer = CreateHeartbeatTimer(hostIsRunning: false))
            {
                timer.Start(executeFirst: true);

                try
                {
                    logItem = _hostContext.ExecuteFunction.Execute(instance, cancellationToken);
                }
                finally
                {
                    timer.Stop();
                }
            }

            VerifySuccess(logItem);
        }

        private IntervalSeparationTimer CreateHeartbeatTimer(bool hostIsRunning)
        {
            ICanFailCommand heartbeat = CreateHeartbeat(hostIsRunning);
            return LinearSpeedupTimerCommand.CreateTimer(heartbeat, RunningHost.HeartbeatSignalInterval, TimeSpan.FromSeconds(10));
        }

        private ICanFailCommand CreateHeartbeat(bool hostIsRunning)
        {
            ICanFailCommand terminationCommand = CreateTerminateProcessUponRequestCommand();

            if (!hostIsRunning)
            {
                return terminationCommand;
            }
            else
            {
                ICanFailCommand runningHostHeartbeat = CreateRunningHostHeartbeat();
                ICanFailCommand runningHostInstanceHeartbeat = CreateRunningHostInstanceHeartbeat();
                return new CompositeCanFailCommand(terminationCommand, runningHostHeartbeat, runningHostInstanceHeartbeat);
            }
        }

        private UpdateHostHeartbeatCommand CreateRunningHostHeartbeat()
        {
            return CreateUpdateHostHeartbeatCommand(_hostContext.HostId);
        }

        private UpdateHostHeartbeatCommand CreateRunningHostInstanceHeartbeat()
        {
            return CreateUpdateHostHeartbeatCommand(_hostContext.HostInstanceId);
        }

        private UpdateHostHeartbeatCommand CreateUpdateHostHeartbeatCommand(Guid hostOrInstanceId)
        {
            return new UpdateHostHeartbeatCommand(_hostContext.RunningHostTableWriter, hostOrInstanceId);
        }

        private TerminateProcessUponRequestCommand CreateTerminateProcessUponRequestCommand()
        {
            return new TerminateProcessUponRequestCommand(_hostContext.TerminationSignalReader, _hostContext.HostInstanceId);
        }

        // Throw if the function failed. 
        private static void VerifySuccess(ExecutionInstanceLogEntity logItem)
        {
            if (logItem.GetStatusWithoutHeartbeat() == FunctionInstanceStatus.CompletedFailed)
            {
                throw new Exception("Function failed: " + logItem.ExceptionMessage);
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
                MethodInfoFunctionLocation loc = func.Location as MethodInfoFunctionLocation;
                if (loc != null)
                {
                    if (loc.MethodInfo.Equals(method))
                    {
                        return func;
                    }
                }
            }

            string msg = String.Format("'{0}' can't be invoked from Azure Jobs. Is it missing Azure Jobs bindings?", method);
            throw new InvalidOperationException(msg);
        }
    }
}
