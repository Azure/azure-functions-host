// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Queues.Listeners;
using Microsoft.Azure.Jobs.Host.Timers;
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
        private readonly CloudStorageAccount _dashboardAccount;

        // The user account that we listen on.
        // This is the account that the bindings resolve against.
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;

        private readonly JobHostContext _hostContext;

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

            IStorageAccountProvider accountProvider = serviceProvider.GetStorageAccountProvider();
            IStorageCredentialsValidator credentialsValidator = serviceProvider.GetStorageCredentialsValidator();

            _storageAccount = accountProvider.GetAccount(ConnectionStringNames.Storage);
            // This will make a network call to verify the credentials work.
            credentialsValidator.ValidateCredentials(_storageAccount);
            _dashboardAccount = accountProvider.GetAccount(ConnectionStringNames.Dashboard);

            // Avoid double-validating the same credentials.
            if (_storageAccount != null && _storageAccount.Credentials != null && _dashboardAccount != null &&
                !_storageAccount.Credentials.Equals(_dashboardAccount.Credentials))
            {
                // This will make a network call to verify the credentials work.
                credentialsValidator.ValidateCredentials(_dashboardAccount);
            }

            IConnectionStringProvider connectionStringProvider = serviceProvider.GetConnectionStringProvider();
            _serviceBusConnectionString = connectionStringProvider.GetConnectionString(ConnectionStringNames.ServiceBus);

            // This will do heavy operations like indexing. 
            _hostContext = GetHostContext(serviceProvider.GetTypeLocator(), serviceProvider.GetNameResolver());
        }

        private JobHostContext GetHostContext(ITypeLocator typesLocator, INameResolver nameResolver)
        {
            var hostContext = new JobHostContext(_dashboardAccount, _storageAccount, _serviceBusConnectionString, typesLocator, nameResolver);
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
            Console.WriteLine("Job host started");

            RunAndBlock(token, () =>
            {
                Thread.Sleep(2 * 1000);
            });

            Console.WriteLine("Job host stopped");
        }

        // Run the jobs on the current thread. 
        // Execute as much work as possible, and then invoke pauseAction() when there's a pause in the work. 
        internal void RunAndBlock(CancellationToken token, Action pauseAction)
        {
            using (WebJobsShutdownWatcher watcher = new WebJobsShutdownWatcher())
            using (IntervalSeparationTimer timer = CreateHeartbeatTimer())
            {
                timer.Start(executeFirst: true);

                token = CancellationTokenSource.CreateLinkedTokenSource(token, watcher.Token).Token;

                HostBindingContext context = new HostBindingContext(
                    bindingProvider: _hostContext.BindingProvider,
                    cancellationToken: token,
                    nameResolver: _hostContext.NameResolver,
                    storageAccount: _storageAccount,
                    serviceBusConnectionString: _serviceBusConnectionString);
                IFunctionExecutor executor = new FunctionExecutor(_hostContext.ExecutionContext, context);

                CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
                IListener sharedQueueListener;
                IListener instanceQueueListener;

                if (_dashboardAccount != null)
                {
                    sharedQueueListener = HostMessageListener.Create(
                        queueClient.GetQueueReference(_hostContext.SharedQueueName),
                        executor,
                        _hostContext.FunctionLookup,
                        _hostContext.FunctionInstanceLogger,
                        context);
                    instanceQueueListener = HostMessageListener.Create(
                        queueClient.GetQueueReference(_hostContext.InstanceQueueName),
                        executor,
                        _hostContext.FunctionLookup,
                        _hostContext.FunctionInstanceLogger,
                        context);
                }
                else
                {
                    sharedQueueListener = null;
                    instanceQueueListener = null;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                using (IListener listener = CreateListener(executor, context,
                    _hostContext.Functions.ReadAll(), sharedQueueListener, instanceQueueListener))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    listener.Start();

                    while (!token.IsCancellationRequested)
                    {
                        pauseAction();
                    }

                    listener.Stop();
                }

                timer.Stop();
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

            IFunctionDefinition func = ResolveFunctionDefinition(method, _hostContext.FunctionLookup);
            IDelayedException exception;

            using (WebJobsShutdownWatcher watcher = new WebJobsShutdownWatcher())
            {
                cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, watcher.Token).Token;
                HostBindingContext context = new HostBindingContext(
                    bindingProvider: _hostContext.BindingProvider,
                    cancellationToken: cancellationToken,
                    nameResolver: _hostContext.NameResolver,
                    storageAccount: _storageAccount,
                    serviceBusConnectionString: _serviceBusConnectionString);
                IFunctionExecutor executor = new FunctionExecutor(_hostContext.ExecutionContext, context);
                IFunctionInstance instance = CreateFunctionInstance(func, arguments, context);

                exception = executor.TryExecute(instance);
            }

            if (exception != null)
            {
                exception.Throw();
            }
        }

        private static IFunctionInstance CreateFunctionInstance(IFunctionDefinition func,
            IDictionary<string, object> parameters, HostBindingContext context)
        {
            return func.InstanceFactory.Create(Guid.NewGuid(), null, ExecutionReason.HostCall, parameters);
        }

        private IntervalSeparationTimer CreateHeartbeatTimer()
        {
            ICanFailCommand heartbeatCommand = new UpdateHostHeartbeatCommand(_hostContext.HeartbeatCommand);
            return LinearSpeedupTimerCommand.CreateTimer(heartbeatCommand,
                HeartbeatIntervals.NormalSignalInterval, HeartbeatIntervals.MinimumSignalInterval);
        }

        private static IListener CreateListener(IFunctionExecutor executor, HostBindingContext context,
            IEnumerable<IFunctionDefinition> functionDefinitions, IListener sharedQueueListener,
            IListener instanceQueueListener)
        {
            List<IListener> listeners = new List<IListener>();
            ListenerFactoryContext listenerContext = new ListenerFactoryContext(context, new SharedListenerContainer());

            foreach (IFunctionDefinition functionDefinition in functionDefinitions)
            {
                IListenerFactory listenerFactory = functionDefinition.ListenerFactory;

                if (listenerFactory == null)
                {
                    continue;
                }

                IListener listener = listenerFactory.Create(executor, listenerContext);
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

        private static JobHostConfiguration ThrowIfNull(JobHostConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            return configuration;
        }

        private IFunctionDefinition ResolveFunctionDefinition(MethodInfo method, IFunctionIndexLookup functionLookup)
        {
            IFunctionDefinition function = functionLookup.Lookup(method);

            if (function == null)
            {
                string msg = String.Format("'{0}' can't be invoked from Azure Jobs. Is it missing Azure Jobs bindings?", method);
                throw new InvalidOperationException(msg);
            }

            return function;
        }
    }
}
