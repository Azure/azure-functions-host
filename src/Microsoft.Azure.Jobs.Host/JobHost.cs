// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
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
    public class JobHost
    {
        private readonly JobHostContextFactory _contextFactory;

        private JobHostContext _context;
        private bool _contextInitialized;
        private object _contextLock = new object();

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
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        public void RunOnBackgroundThread(CancellationToken cancellationToken)
        {
            JobHostContext hostContext = EnsureHostStarted();

            Console.WriteLine("Job host started");

            IRunner runner = hostContext.RunnerFactory.CreateAndStart(listenForAbortOnly: false,
                cancellationToken: cancellationToken);
            CancellationToken runnerCancellationToken = runner.CancellationToken;

            if (!runnerCancellationToken.CanBeCanceled)
            {
                Thread backgroundThread = new Thread(() =>
                {
                    try
                    {
                        runnerCancellationToken.WaitHandle.WaitOne();
                        runner.Stop();
                    }
                    finally
                    {
                        runner.Dispose();
                    }

                    Console.WriteLine("Job host stopped");
                });

                backgroundThread.Start();
            }
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
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        public void RunAndBlock(CancellationToken cancellationToken)
        {
            JobHostContext hostContext = EnsureHostStarted();

            Console.WriteLine("Job host started");

            using (IRunner runner = hostContext.RunnerFactory.CreateAndStart(listenForAbortOnly: false,
                cancellationToken: cancellationToken))
            {
                CancellationToken runnerCancellationToken = runner.CancellationToken;

                if (!runnerCancellationToken.CanBeCanceled)
                {
                    Thread.Sleep(Timeout.Infinite);
                }
                else
                {
                    runnerCancellationToken.WaitHandle.WaitOne();
                }

                runner.Stop();
            }

            Console.WriteLine("Job host stopped");
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
        /// <param name="arguments">
        /// An object with public properties representing argument names and values to bind to the parameter tokens in
        /// the job method's arguments.
        /// </param>
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

            JobHostContext hostContext = EnsureHostStarted();
            IFunctionDefinition function = ResolveFunctionDefinition(method, hostContext.FunctionLookup);
            IFunctionInstance instance = CreateFunctionInstance(function, arguments);
            IDelayedException exception;

            using (IRunner runner = hostContext.RunnerFactory.CreateAndStart(listenForAbortOnly: true,
                cancellationToken: cancellationToken))
            {
                IFunctionExecutor executor = runner.Executor;
                exception = executor.TryExecute(instance);

                runner.Stop();
            }

            if (exception != null)
            {
                exception.Throw();
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

        private JobHostContext CreateContextAndLogHostStarted()
        {
            return _contextFactory.CreateAndLogHostStarted();
        }

        private JobHostContext EnsureHostStarted()
        {
            return LazyInitializer.EnsureInitialized<JobHostContext>(ref _context, ref _contextInitialized,
                ref _contextLock, CreateContextAndLogHostStarted);
        }
    }
}
