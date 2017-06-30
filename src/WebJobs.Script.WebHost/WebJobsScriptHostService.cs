using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebJobs.Script.WebHost.Core;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Core
{
    public class WebJobsScriptHostService : IHostedService, IDisposable
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _disposed = false;
        private Task _hostTask;
        private readonly ILogger<WebJobsScriptHostService> _logger;

        public WebJobsScriptHostService(WebScriptHostManager scriptHostManager, ILoggerFactory loggerFactory)
        {
            _scriptHostManager = scriptHostManager ?? throw new ArgumentException($@"Unable to locate the {nameof(WebScriptHostManager)} service. " +
                    $"Please add all the required services by calling '{nameof(IServiceCollection)}.{nameof(WebJobsServiceCollectionExtensions.AddWebJobsScriptHost)}' " +
                    $"inside the call to 'ConfigureServices' in the application startup code");

            _cancellationTokenSource = new CancellationTokenSource();
            _hostTask = Task.CompletedTask;
            _logger = loggerFactory.CreateLogger<WebJobsScriptHostService>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _hostTask = Task.Run(() =>
            {
                _logger.LogDebug("Initializing WebScriptHostManager.");
                _scriptHostManager.Initialize(_cancellationTokenSource.Token);
                _logger.LogDebug("WebScriptHostManager initialized.");
            });

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource.Cancel();

            Task result = await Task.WhenAny(_hostTask, Task.Delay(TimeSpan.FromSeconds(10)));

            if (result != _hostTask)
            {
                _logger.LogWarning("Script host manager did not shutdown within its allotted time.");
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
