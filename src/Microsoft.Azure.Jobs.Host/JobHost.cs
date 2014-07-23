// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Defines properties and methods to locate Job methods and listen to trigger events in order
    /// to execute Job methods.
    /// </summary>
    public class JobHost : IDisposable
    {
        private readonly JobHostContextFactory _contextFactory;

        private Task<JobHostContext> _contextTask;
        private bool _contextTaskInitialized;
        private object _contextTaskLock = new object();

        private IRunner _runner;
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

            _contextFactory = new JobHostContextFactory(dashboardAccount, storageAccount, serviceBusConnectionString,
                credentialsValidator, typeLocator, nameResolver);
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

            if (_runner != null)
            {
                return Task.FromResult(0);
            }

            return StartAsyncCore(cancellationToken);
        }

        private async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            JobHostContext context = await EnsureHostStartedAsync(cancellationToken);
            _runner = await context.RunnerFactory.CreateAndStartAsync(listenForAbortOnly: false,
                cancellationToken: cancellationToken);

            Console.WriteLine("Job host started");
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

            if (_runner == null)
            {
                return Task.FromResult(0);
            }

            return StopAsyncCore(CancellationToken.None);
        }

        private async Task StopAsyncCore(CancellationToken cancellationToken)
        {
            if (_runner != null)
            {
                await _runner.StopAsync(cancellationToken);

                Console.WriteLine("Job host stopped");
                _runner = null;
            }
        }

        /// <summary>Runs the host and blocks the current thread while the host remains running.</summary>
        public void RunAndBlock()
        {
            Start();

            // Wait for someone to begin shut down (either Stop or _shutdownWatcher).
            _runner.HostCancellationToken.WaitHandle.WaitOne();

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
            JobHostContext hostContext = await EnsureHostStartedAsync(cancellationToken);
            IFunctionDefinition function = ResolveFunctionDefinition(method, hostContext.FunctionLookup);
            IFunctionInstance instance = CreateFunctionInstance(function, arguments);
            IDelayedException exception;

            using (IRunner runner = await hostContext.RunnerFactory.CreateAndStartAsync(listenForAbortOnly: true,
                cancellationToken: cancellationToken))
            using (cancellationToken.Register(runner.Cancel))
            {
                IFunctionExecutor executor = runner.Executor;
                exception = await executor.TryExecuteAsync(instance, runner.HostCancellationToken);
                await runner.StopAsync(cancellationToken);
            }

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
                if (_runner != null)
                {
                    _runner.Dispose();
                    _runner = null;
                }

                _disposed = true;
            }
        }

        private static IFunctionInstance CreateFunctionInstance(IFunctionDefinition func,
            IDictionary<string, object> parameters)
        {
            return func.InstanceFactory.Create(Guid.NewGuid(), null, ExecutionReason.HostCall, parameters);
        }

        private static IFunctionDefinition ResolveFunctionDefinition(MethodInfo method, IFunctionIndexLookup functionLookup)
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

        private Task<JobHostContext> CreateContextAndLogHostStartedAsync(CancellationToken cancellationToken)
        {
            return _contextFactory.CreateAndLogHostStartedAsync(cancellationToken);
        }

        private Task<JobHostContext> EnsureHostStartedAsync(CancellationToken cancellationToken)
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
