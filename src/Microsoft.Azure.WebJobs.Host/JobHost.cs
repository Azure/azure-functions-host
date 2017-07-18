// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// A <see cref="JobHost"/> is the execution container for jobs. Once started, the
    /// <see cref="JobHost"/> will manage and run job functions when they are triggered.
    /// </summary>
    public class JobHost : IDisposable
    {
        private const int StateNotStarted = 0;
        private const int StateStarting = 1;
        private const int StateStarted = 2;
        private const int StateStoppingOrStopped = 3;

        private readonly JobHostConfiguration _config;
        private readonly CancellationTokenSource _shutdownTokenSource;
        private readonly WebJobsShutdownWatcher _shutdownWatcher;
        private readonly CancellationTokenSource _stoppingTokenSource;

        private JobHostContext _context;
        private IListener _listener;

        // Null if we haven't yet started runtime initialization.
        // Points to an incomplete task during initialization. 
        // Points to a completed task after initialization. 
        private Task _initializationRunning = null;

        // These are services that are accessible without starting the execution container. 
        // They include the initial set of JobHostConfiguration services as well as 
        // additional services created. 
        private ServiceProviderWrapper _services;

        private int _state;
        private Task _stopTask;
        private bool _disposed;

        // Common lock to protect fields. 
        private object _lock = new object();

        private ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHost"/> class, using a Microsoft Azure Storage connection
        /// string located in the connectionStrings section of the configuration file or in environment variables.
        /// </summary>
        public JobHost()
            : this(new JobHostConfiguration())
        {
        }

        static JobHost()
        {
            // add webjobs to user agent for all storage calls
            OperationContext.GlobalSendingRequest += (sender, e) =>
            {
                // TODO: FACAVAL - This is not supported on by the latest version of the
                // storage SDK. Need to re-add this when the capability is reintroduced.
                // e.UserAgent += " AzureWebJobs";
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHost"/> class using the configuration provided.
        /// </summary>
        /// <param name="configuration">The job host configuration.</param>
        public JobHost(JobHostConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            _config = configuration;
            _shutdownTokenSource = new CancellationTokenSource();
            _shutdownWatcher = WebJobsShutdownWatcher.Create(_shutdownTokenSource);
            _stoppingTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_shutdownTokenSource.Token);
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
            Task.Run(() => StartAsync()).GetAwaiter().GetResult();
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

            string msg = "Job host started";
            _context.Trace.Info(msg, Host.TraceSource.Host);
            _logger?.LogInformation(msg);

            _state = StateStarted;
        }

        /// <summary>Stops the host.</summary>
        public void Stop()
        {
            Task.Run(() => StopAsync()).GetAwaiter().GetResult();
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
            lock (_lock)
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

            // Flush remaining logs
            var functionEventCollector = _context.FunctionEventCollector;
            if (functionEventCollector != null)
            {
                await functionEventCollector.FlushAsync(cancellationToken);
            }

            string msg = "Job host stopped";
            _context.Trace.Info(msg, Host.TraceSource.Host);
            _logger?.LogInformation(msg);
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
            Task.Run(() => CallAsync(method)).GetAwaiter().GetResult();
        }

        /// <summary>Calls a job method.</summary>
        /// <param name="method">The job method to call.</param>
        /// <param name="arguments">
        /// An object with public properties representing argument names and values to bind to parameters in the job
        /// method. In addition to parameter values, these may also include binding data values.
        /// </param>
        public void Call(MethodInfo method, object arguments)
        {
            Task.Run(() => CallAsync(method, arguments)).GetAwaiter().GetResult();
        }

        /// <summary>Calls a job method.</summary>
        /// <param name="method">The job method to call.</param>
        /// <param name="arguments">The argument names and values to bind to parameters in the job method.
        /// In addition to parameter values, these may also include binding data values. </param>
        public void Call(MethodInfo method, IDictionary<string, object> arguments)
        {
            Task.Run(() => CallAsync(method, arguments)).GetAwaiter().GetResult();
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
        /// method. In addition to parameter values, these may also include binding data values. 
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
        /// <param name="arguments">The argument names and values to bind to parameters in the job method. In addition to parameter values, these may also include binding data values. </param>
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

        /// <summary>Calls a job method.</summary>
        /// <param name="name">The name of the function to call.</param>
        /// <param name="arguments">The argument names and values to bind to parameters in the job method. In addition to parameter values, these may also include binding data values. </param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will call the job method.</returns>
        public async Task CallAsync(string name, IDictionary<string, object> arguments = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            ThrowIfDisposed();

            await EnsureHostStartedAsync(cancellationToken);

            IFunctionDefinition function = _context.FunctionLookup.LookupByName(name);
            Validate(function, name);
            IFunctionInstance instance = CreateFunctionInstance(function, arguments);

            IDelayedException exception = await _context.Executor.TryExecuteAsync(instance, cancellationToken);

            if (exception != null)
            {
                exception.Throw();
            }
        }

        private async Task CallAsyncCore(MethodInfo method, IDictionary<string, object> arguments,
            CancellationToken cancellationToken)
        {
            await EnsureHostStartedAsync(cancellationToken);
            IFunctionDefinition function = _context.FunctionLookup.Lookup(method);
            Validate(function, method);
            IFunctionInstance instance = CreateFunctionInstance(function, arguments);

            IDelayedException exception = await _context.Executor.TryExecuteAsync(instance, cancellationToken);

            if (exception != null)
            {
                exception.Throw();
            }
        }

        /// <summary>
        /// Dispose the instance
        /// </summary>
        /// <param name="disposing">True if currently disposing.</param>
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "_shutdownTokenSource")]
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
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

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private static IFunctionInstance CreateFunctionInstance(IFunctionDefinition func, IDictionary<string, object> parameters)
        {
            var context = new FunctionInstanceFactoryContext
            {
                Id = Guid.NewGuid(),
                ParentId = null,
                ExecutionReason = ExecutionReason.HostCall,
                Parameters = parameters
            };
            return func.InstanceFactory.Create(context);
        }

        private static void Validate(IFunctionDefinition function, object key)
        { 
            if (function == null)
            {
                string msg = String.Format(CultureInfo.CurrentCulture, "'{0}' can't be invoked from Azure WebJobs SDK. Is it missing Azure WebJobs SDK attributes?", key);
                throw new InvalidOperationException(msg);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }

        // If multiple threads call this, only one should do the init. The rest should wait.
        // When this task is signalled, _context is initialized. 
        private Task EnsureHostStartedAsync(CancellationToken cancellationToken)
        {
            if (_context != null)
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> tsc = null;

            lock (_lock)
            {
                if (_initializationRunning == null)
                {
                    // This thread wins the race and owns initialing. 
                    tsc = new TaskCompletionSource<bool>();
                    _initializationRunning = tsc.Task;
                }
            }

            if (tsc != null)
            {
                // Ignore the return value and use tsc so that all threads are awaiting the same thing. 
                Task ignore = RuntimeInitAsync(cancellationToken, tsc);
            }

            return _initializationRunning;
        }

        // Caller gaurantees this is single-threaded. 
        // Set initializationTask when complete, many threads can wait on that. 
        // When complete, the fields should be initialized to allow runtime usage. 
        private async Task RuntimeInitAsync(CancellationToken cancellationToken, TaskCompletionSource<bool> initializationTask)
        {
            try
            {
                // Do real initialization 
                PopulateStaticServices();

                JobHostContext context = await _config.CreateJobHostContextAsync(_services, this, _shutdownTokenSource.Token, cancellationToken);

                _context = context;
                _listener = context.Listener;
                _logger = _context.LoggerFactory?.CreateLogger(LogCategories.Startup);

                initializationTask.SetResult(true);
            }
            catch (Exception e)
            {
                initializationTask.SetException(e);
            }
        }

        // Ensure the static services are initialized. 
        // These are derived from the underlying JobHostConfiguration. 
        // Caller ensures this is single threaded. 
        private void PopulateStaticServices()
        {
            if (this._services != null)
            {
                return; // already Created 
            }

            var services = this._config.CreateStaticServices();

            _services = services;
        }

        /// <summary>
        /// Get set of services. 
        /// </summary>
        public IServiceProvider Services
        {
            get
            {
                PopulateStaticServices();
                return _services;
            }
        }
    }
}
