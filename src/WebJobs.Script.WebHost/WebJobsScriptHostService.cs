// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebJobsScriptHostService : IHostedService, IScriptHostManager, IDisposable
    {
        private readonly IServiceProvider _rootServiceProvider;
        private readonly IServiceScopeFactory _rootScopeFactory;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IScriptWebHostEnvironment _scriptWebHostEnvironment;
        private readonly ILogger _logger;
        private CancellationTokenSource _startupLoopTokenSource;
        private bool _disposed = false;
        private IHost _host;

        public WebJobsScriptHostService(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IServiceProvider rootServiceProvider,
            IServiceScopeFactory rootScopeFactory, IScriptWebHostEnvironment scriptWebHostEnvironment, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));
            _rootScopeFactory = rootScopeFactory ?? throw new ArgumentNullException(nameof(rootScopeFactory));
            _applicationHostOptions = applicationHostOptions ?? throw new ArgumentNullException(nameof(applicationHostOptions));
            _scriptWebHostEnvironment = scriptWebHostEnvironment ?? throw new ArgumentNullException(nameof(scriptWebHostEnvironment));

            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

            State = ScriptHostState.Default;
        }

        public IServiceProvider Services => _host?.Services;

        public ScriptHostState State { get; private set; }

        public Exception LastError { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _startupLoopTokenSource = new CancellationTokenSource();
            var startupLoopToken = _startupLoopTokenSource.Token;
            var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(startupLoopToken, cancellationToken);

            try
            {
                await StartHostAsync(tokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Initialization cancellation requested by runtime.");
                    throw;
                }

                // If the exception was triggered by our loop cancellation token, just ignore as
                // it doesn't indicate an issue.
            }
        }

        private async Task StartHostAsync(CancellationToken cancellationToken, int attemptCount = 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                bool isOffline = CheckAppOffline();
                _host = BuildHost(isOffline);

                var log = isOffline ? "Host is offline." : "Initializing Host.";
                _logger.LogInformation(log);
                await _host.StartAsync(cancellationToken);
                LastError = null;

                if (!isOffline)
                {
                    State = ScriptHostState.Running;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exc)
            {
                LastError = exc;
                State = ScriptHostState.Error;
                attemptCount++;

                var hostLoggerFactory = _host?.Services.GetService<ILoggerFactory>();
                var logger = hostLoggerFactory?.CreateLogger(LogCategories.Startup) ?? _logger;

                logger.LogError(exc, "A ScriptHost error has occurred");

                var orphanTask = Orphan(_host, logger)
                    .ContinueWith(t =>
                           {
                               if (t.IsFaulted)
                               {
                                   t.Exception.Handle(e => true);
                               }
                           }, TaskContinuationOptions.ExecuteSynchronously);

                cancellationToken.ThrowIfCancellationRequested();

                await Utility.DelayWithBackoffAsync(attemptCount, cancellationToken, min: TimeSpan.FromSeconds(1), max: TimeSpan.FromMinutes(2))
                    .ContinueWith(t =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return StartHostAsync(cancellationToken, attemptCount);
                    });
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _startupLoopTokenSource?.Cancel();

            State = ScriptHostState.Stopping;
            _logger.LogInformation("Stopping host...");

            var currentHost = _host;
            Task stopTask = Orphan(currentHost, _logger, cancellationToken);
            Task result = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10)));

            if (result != stopTask)
            {
                _logger.LogWarning("Host did not shutdown within its allotted time.");
            }
            else
            {
                _logger.LogInformation("Host shutdown completed.");
            }

            State = ScriptHostState.Stopped;
        }

        public async Task RestartHostAsync(CancellationToken cancellationToken)
        {
            if (State == ScriptHostState.Stopping || State == ScriptHostState.Stopped)
            {
                return;
            }

            _startupLoopTokenSource?.Cancel();

            State = ScriptHostState.Default;

            _logger.LogInformation("Restarting script host.");

            var previousHost = _host;
            Task startTask = StartAsync(cancellationToken);
            Task stopTask = Orphan(previousHost, _logger, cancellationToken);

            await startTask;

            _logger.LogInformation("Script host restarted.");
        }

        private IHost BuildHost(bool isOffline = false)
        {
            var builder = new HostBuilder()
                .SetAzureFunctionsEnvironment()
                .AddWebScriptHost(_rootServiceProvider, _rootScopeFactory, _applicationHostOptions.CurrentValue);

            if (isOffline)
            {
                builder.ConfigureServices(services =>
                {
                    // When offline, we need most general services registered so admin
                    // APIs can function. However, we want to prevent the ScriptHost from
                    // actually starting up. To accomplish this, we remove the host service
                    // responsible for starting the job hst.
                    var jobHostService = services.FirstOrDefault(p => p.ImplementationType == typeof(JobHostService));
                    services.Remove(jobHostService);
                });
            }

            return builder.Build();
        }

        private bool CheckAppOffline()
        {
            // check if we should be in an offline state
            string offlineFilePath = Path.Combine(_applicationHostOptions.CurrentValue.ScriptPath, ScriptConstants.AppOfflineFileName);
            if (File.Exists(offlineFilePath))
            {
                State = ScriptHostState.Offline;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Remove the <see cref="IHost"/> instance from the live instances collection,
        /// allowing it to finish currently executing functions before stopping and disposing of it.
        /// </summary>
        /// <param name="instance">The <see cref="IHost"/> instance to remove</param>
        private async Task Orphan(IHost instance, ILogger logger, CancellationToken cancellationToken = default)
        {
            try
            {
                await instance?.StopAsync(cancellationToken);
            }
            catch (Exception)
            {
                //  logger.LogTrace(exc, "Error stopping and disposing of host");
            }
            finally
            {
                instance?.Dispose();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _startupLoopTokenSource?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose() => Dispose(true);
    }
}
