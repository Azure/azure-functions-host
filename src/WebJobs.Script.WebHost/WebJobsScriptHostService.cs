// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebJobsScriptHostService : IHostedService, IDisposable
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ILogger _logger;
        private bool _disposed = false;
        private Task _hostTask;

        public WebJobsScriptHostService(WebScriptHostManager scriptHostManager, ILoggerFactory loggerFactory)
        {
            _scriptHostManager = scriptHostManager ?? throw new ArgumentException($@"Unable to locate the {nameof(WebScriptHostManager)} service. " +
                    $"Please add all the required services by calling '{nameof(IServiceCollection)}.{nameof(WebJobsServiceCollectionExtensions.AddWebJobsScriptHost)}' " +
                    $"inside the call to 'ConfigureServices' in the application startup code");

            _cancellationTokenSource = new CancellationTokenSource();
            _hostTask = Task.CompletedTask;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_scriptHostManager.State != ScriptHostState.Offline)
            {
                _logger.LogInformation("Initializing Host.");
                _hostTask = _scriptHostManager.EnsureHostStarted(_cancellationTokenSource.Token);
            }
            else
            {
                _logger.LogInformation("Host is offline and will not be initialized.");
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource.Cancel();

            if (_scriptHostManager.State != ScriptHostState.Offline)
            {
                Task result = await Task.WhenAny(_hostTask, Task.Delay(TimeSpan.FromSeconds(10)));
                if (result != _hostTask)
                {
                    _logger.LogWarning("Host did not shutdown within its allotted time.");
                }
                else
                {
                    _logger.LogInformation("Host shutdown completed.");
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cancellationTokenSource.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose() => Dispose(true);
    }
}
