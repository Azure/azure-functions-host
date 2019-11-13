// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class HttpFunctionInvocationDispatcher : IFunctionInvocationDispatcher
    {
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger _logger;
        private readonly IHttpWorkerChannelFactory _httpWorkerChannelFactory;
        private readonly IScriptJobHostEnvironment _scriptJobHostEnvironment;
        private readonly TimeSpan thresholdBetweenRestarts = TimeSpan.FromMinutes(WorkerConstants.WorkerRestartErrorIntervalThresholdInMinutes);

        private IScriptEventManager _eventManager;
        private IDisposable _workerErrorSubscription;
        private IDisposable _workerRestartSubscription;
        private ScriptJobHostOptions _scriptOptions;
        private bool _disposed = false;
        private bool _disposing = false;
        private ConcurrentStack<HttpWorkerErrorEvent> _invokerErrors = new ConcurrentStack<HttpWorkerErrorEvent>();
        private IHttpWorkerChannel _httpWorkerChannel;

        public HttpFunctionInvocationDispatcher(IOptions<ScriptJobHostOptions> scriptHostOptions,
            IMetricsLogger metricsLogger,
            IScriptJobHostEnvironment scriptJobHostEnvironment,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IHttpWorkerChannelFactory httpWorkerChannelFactory)
        {
            _metricsLogger = metricsLogger;
            _scriptOptions = scriptHostOptions.Value;
            _scriptJobHostEnvironment = scriptJobHostEnvironment;
            _eventManager = eventManager;
            _logger = loggerFactory.CreateLogger<HttpFunctionInvocationDispatcher>();
            _httpWorkerChannelFactory = httpWorkerChannelFactory ?? throw new ArgumentNullException(nameof(httpWorkerChannelFactory));

            State = FunctionInvocationDispatcherState.Default;

            _workerErrorSubscription = _eventManager.OfType<HttpWorkerErrorEvent>()
               .Subscribe(WorkerError);
            _workerRestartSubscription = _eventManager.OfType<HttpWorkerRestartEvent>()
               .Subscribe(WorkerRestart);
        }

        public FunctionInvocationDispatcherState State { get; private set; }

        internal Task InitializeHttpeWorkerChannelAsync(int attemptCount)
        {
            // TODO: Add process managment for http invoker
            _httpWorkerChannel = _httpWorkerChannelFactory.Create(_scriptOptions.RootScriptPath, _metricsLogger, attemptCount);
            _httpWorkerChannel.StartWorkerProcessAsync().ContinueWith(workerInitTask =>
                 {
                     if (workerInitTask.IsCompleted)
                     {
                         _logger.LogDebug("Adding http worker channel. workerId:{id}", _httpWorkerChannel.Id);
                         State = FunctionInvocationDispatcherState.Initialized;
                     }
                     else
                     {
                         _logger.LogWarning("Failed to start http worker process. workerId:{id}", _httpWorkerChannel.Id);
                     }
                 });
            return Task.CompletedTask;
        }

        public async Task InitializeAsync(IEnumerable<FunctionMetadata> functions)
        {
            if (functions == null || !functions.Any())
            {
                // do not initialize function dispachter if there are no functions
                return;
            }

            State = FunctionInvocationDispatcherState.Initializing;
            await InitializeHttpeWorkerChannelAsync(0);
        }

        public Task InvokeAsync(ScriptInvocationContext invocationContext)
        {
            return _httpWorkerChannel.InvokeFunction(invocationContext);
        }

        public async void WorkerError(HttpWorkerErrorEvent workerError)
        {
            if (!_disposing)
            {
                _logger.LogDebug("Handling WorkerErrorEvent for workerId:{workerId}", workerError.WorkerId);
                AddOrUpdateErrorBucket(workerError);
                await DisposeAndRestartWorkerChannel(workerError.WorkerId);
            }
        }

        public async void WorkerRestart(HttpWorkerRestartEvent workerRestart)
        {
            if (!_disposing)
            {
                _logger.LogDebug("Handling WorkerRestartEvent for workerId:{workerId}", workerRestart.WorkerId);
                await DisposeAndRestartWorkerChannel(workerRestart.WorkerId);
            }
        }

        private async Task DisposeAndRestartWorkerChannel(string workerId)
        {
                _logger.LogDebug("Disposing channel for workerId: {channelId}", workerId);
                if (_httpWorkerChannel != null)
                {
                    (_httpWorkerChannel as IDisposable)?.Dispose();
                }
                await RestartWorkerChannel(workerId);
        }

        private async Task RestartWorkerChannel(string workerId)
        {
            if (_invokerErrors.Count < 3)
            {
                _logger.LogDebug("Restarting http invoker channel");
                await InitializeHttpeWorkerChannelAsync(_invokerErrors.Count);
            }
            else
            {
                _logger.LogError("Exceeded http worker restart retry count. Shutting down Functions Host");
                _scriptJobHostEnvironment.Shutdown();
            }
        }

        private void AddOrUpdateErrorBucket(HttpWorkerErrorEvent currentErrorEvent)
        {
            if (_invokerErrors.TryPeek(out HttpWorkerErrorEvent top))
            {
                if ((currentErrorEvent.CreatedAt - top.CreatedAt) > thresholdBetweenRestarts)
                {
                    while (!_invokerErrors.IsEmpty)
                    {
                        _invokerErrors.TryPop(out HttpWorkerErrorEvent popped);
                        _logger.LogDebug($"Popping out errorEvent createdAt:{popped.CreatedAt} workerId:{popped.WorkerId}");
                    }
                }
            }
            _invokerErrors.Push(currentErrorEvent);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _logger.LogDebug($"Disposing {nameof(HttpFunctionInvocationDispatcher)}");
                _workerErrorSubscription.Dispose();
                _workerRestartSubscription.Dispose();
                (_httpWorkerChannel as IDisposable)?.Dispose();
                _disposed = true;
            }
        }

        public void Dispose()
        {
            _disposing = true;
            Dispose(true);
        }

        public Task ShutdownAsync()
        {
            return Task.CompletedTask;
        }
    }
}
