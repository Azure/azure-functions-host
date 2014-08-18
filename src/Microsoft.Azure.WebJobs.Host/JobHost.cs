// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Defines properties and methods to locate Job methods and listen to trigger events in order
    /// to execute Job methods.
    /// </summary>
    public class JobHost : IDisposable
    {
        private const int StateNotStarted = 0;
        private const int StateStarting = 1;
        private const int StateStarted = 2;
        private const int StateStoppingOrStopped = 3;

        private readonly JobHostContextFactory _contextFactory;
        private readonly CancellationTokenSource _shutdownTokenSource;
        private readonly WebJobsShutdownWatcher _shutdownWatcher;
        private readonly CancellationTokenSource _stoppingTokenSource;

        private Task<JobHostContext> _contextTask;
        private bool _contextTaskInitialized;
        private object _contextTaskLock = new object();

        private JobHostContext _context;
        private IListener _listener;
        private object _contextLock = new object();

        private int _state;
        private Task _stopTask;
        private object _stopTaskLock = new object();
        private bool _disposed;

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
            CloudStorageAccount dashboardAccount = accountProvider.GetAccount(ConnectionStringNames.Dashboard);
            CloudStorageAccount storageAccount = accountProvider.GetAccount(ConnectionStringNames.Storage);

            IConnectionStringProvider connectionStringProvider = serviceProvider.GetConnectionStringProvider();
            string serviceBusConnectionString =
                connectionStringProvider.GetConnectionString(ConnectionStringNames.ServiceBus);

            IStorageCredentialsValidator credentialsValidator = serviceProvider.GetStorageCredentialsValidator();
            ITypeLocator typeLocator = serviceProvider.GetTypeLocator();
            INameResolver nameResolver = serviceProvider.GetNameResolver();
            IQueueConfiguration queueConfiguration = serviceProvider.GetJobHostQueuesConfiguration();

            _shutdownTokenSource = new CancellationTokenSource();
            _shutdownWatcher = WebJobsShutdownWatcher.Create(_shutdownTokenSource);
            _stoppingTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_shutdownTokenSource.Token);

            _contextFactory = new JobHostContextFactory(dashboardAccount, storageAccount, serviceBusConnectionString,
                credentialsValidator, typeLocator, nameResolver, queueConfiguration, _shutdownTokenSource.Token);
        }

        // Test hook only.
        internal IListener Listener
        {
            get { return _listener; }
            set { _listener = value; }
        }

        /// <summary>Starts the host.</summary>
        public void Start()
        {
            StartAsync().GetAwaiter().GetResult();
        }

        /// <summary>Starts the host.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will start the host.</returns>
        public Task StartAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();

            if (Interlocked.CompareExchange(ref _state, StateStarting, StateNotStarted) != StateNotStarted)
            {
                throw new InvalidOperationException("Start has already been called.");
            }

            return StartAsyncCore(cancellationToken);
        }

        private async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            await EnsureHostStartedAsync(cancellationToken);

            await _listener.StartAsync(cancellationToken);
            Console.WriteLine("Job host started");

            _state = StateStarted;
        }

        /// <summary>Stops the host.</summary>
        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        /// <summary>Stops the host.</summary>
        /// <returns>A <see cref="Task"/> that will stop the host.</returns>
        public Task StopAsync()
        {
            ThrowIfDisposed();

            Interlocked.CompareExchange(ref _state, StateStoppingOrStopped, StateStarted);

            if (_state != StateStoppingOrStopped)
            {
                throw new InvalidOperationException("The host has not yet started.");
            }

            // Multiple threads may call StopAsync concurrently. Both need to return the same task instance.
            lock (_stopTaskLock)
            {
                if (_stopTask == null)
                {
                    _stoppingTokenSource.Cancel();
                    _stopTask = StopAsyncCore(CancellationToken.None);
                }
            }

            return _stopTask;
        }

        private async Task StopAsyncCore(CancellationToken cancellationToken)
        {
            await _listener.StopAsync(cancellationToken);

            Console.WriteLine("Job host stopped");
        }

        /// <summary>Runs the host and blocks the current thread while the host remains running.</summary>
        public void RunAndBlock()
        {
            Start();

            // Wait for someone to begin stopping (_shutdownWatcher, Stop, or Dispose).
            _stoppingTokenSource.Token.WaitHandle.WaitOne();

            // Don't return until all executing functions have completed.
            Stop();
        }

        /// <summary>Calls a job method.</summary>
        /// <param name="method">The job method to call.</param>
        public void Call(MethodInfo method)
        {
            CallAsync(method).GetAwaiter().GetResult();
        }

        /// <summary>Calls a job method.</summary>
        /// <param name="method">The job method to call.</param>
        /// <param name="arguments">
        /// An object with public properties representing argument names and values to bind to parameters in the job
        /// method.
        /// </param>
        public void Call(MethodInfo method, object arguments)
        {
            CallAsync(method, arguments).GetAwaiter().GetResult();
        }

        /// <summary>Calls a job method.</summary>
        /// <param name="method">The job method to call.</param>
        /// <param name="arguments">The argument names and values to bind to parameters in the job method.</param>
        public void Call(MethodInfo method, IDictionary<string, object> arguments)
        {
            CallAsync(method, arguments).GetAwaiter().GetResult();
        }

        /// <summary>Calls a job method.</summary>
        /// <param name="method">The job method to call.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will call the job method.</returns>
        public Task CallAsync(MethodInfo method, CancellationToken cancellationToken = default(CancellationToken))
        {
            IDictionary<string, object> argumentsDictionary = null;
            return CallAsync(method, argumentsDictionary, cancellationToken);
        }

        /// <summary>Calls a job method.</summary>
        /// <param name="method">The job method to call.</param>
        /// <param name="arguments">
        /// An object with public properties representing argument names and values to bind to parameters in the job
        /// method.
        /// </param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will call the job method.</returns>
        public Task CallAsync(MethodInfo method, object arguments,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();

            IDictionary<string, object> argumentsDictionary = ObjectDictionaryConverter.AsDictionary(arguments);
            return CallAsync(method, argumentsDictionary, cancellationToken);
        }

        /// <summary>Calls a job method.</summary>
        /// <param name="method">The job method to call.</param>
        /// <param name="arguments">The argument names and values to bind to parameters in the job method.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will call the job method.</returns>
        public Task CallAsync(MethodInfo method, IDictionary<string, object> arguments,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (method == null)
            {
                throw new ArgumentNullException("method");
            }

            ThrowIfDisposed();

            return CallAsyncCore(method, arguments, cancellationToken);
        }

        private async Task CallAsyncCore(MethodInfo method, IDictionary<string, object> arguments,
            CancellationToken cancellationToken)
        {
            await EnsureHostStartedAsync(cancellationToken);
            IFunctionDefinition function = ResolveFunctionDefinition(method, _context.FunctionLookup);
            IFunctionInstance instance = CreateFunctionInstance(function, arguments);

            IDelayedException exception = await _context.Executor.TryExecuteAsync(instance, cancellationToken);

            if (exception != null)
            {
                exception.Throw();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                // Running callers might still be using this cancellation token.
                // Mark it canceled but don't dispose of the source while the callers are running.
                // Otherwise, callers would receive ObjectDisposedException when calling token.Register.
                // For now, rely on finalization to clean up _shutdownTokenSource's wait handle (if allocated).
                _shutdownTokenSource.Cancel();

                _stoppingTokenSource.Dispose();

                if (_shutdownWatcher != null)
                {
                    _shutdownWatcher.Dispose();
                }

                if (_context != null)
                {
                    _context.Dispose();
                }

                _disposed = true;
            }
        }

        private static IFunctionInstance CreateFunctionInstance(IFunctionDefinition func,
            IDictionary<string, object> parameters)
        {
            return func.InstanceFactory.Create(Guid.NewGuid(), null, ExecutionReason.HostCall, parameters);
        }

        private static IFunctionDefinition ResolveFunctionDefinition(MethodInfo method,
            IFunctionIndexLookup functionLookup)
        {
            IFunctionDefinition function = functionLookup.Lookup(method);

            if (function == null)
            {
                string msg = String.Format(
                    "'{0}' can't be invoked from Azure Jobs. Is it missing Azure Jobs bindings?", method);
                throw new InvalidOperationException(msg);
            }

            return function;
        }

        private static JobHostConfiguration ThrowIfNull(JobHostConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            return configuration;
        }

        private async Task<JobHostContext> CreateContextAndLogHostStartedAsync(CancellationToken cancellationToken)
        {
            JobHostContext context = await _contextFactory.CreateAndLogHostStartedAsync(cancellationToken);

            lock (_contextLock)
            {
                if (_context == null)
                {
                    _context = context;
                    _listener = context.Listener;
                }
            }

            return _context;
        }

        private Task EnsureHostStartedAsync(CancellationToken cancellationToken)
        {
            return LazyInitializer.EnsureInitialized<Task<JobHostContext>>(ref _contextTask,
                ref _contextTaskInitialized,
                ref _contextTaskLock,
                () => CreateContextAndLogHostStartedAsync(cancellationToken));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }
    }
}
