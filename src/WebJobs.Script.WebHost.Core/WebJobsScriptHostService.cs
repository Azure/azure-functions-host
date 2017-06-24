using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebJobs.Script.WebHost.Core;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Core
{
    public class WebJobsScriptHostService : IHostedService, IDisposable
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _disposed = false;
        private Task _hostTask;

        public WebJobsScriptHostService(WebScriptHostManager scriptHostManager)
        {
            _scriptHostManager = scriptHostManager ?? throw new ArgumentException($@"Unable to locate the {nameof(WebScriptHostManager)} service. " +
                    $"Please add all the required services by calling '{nameof(IServiceCollection)}.{nameof(WebJobsServiceCollectionExtensions.AddWebJobsScriptHost)}' " +
                    $"inside the call to 'ConfigureServices' in the application startup code");

            _cancellationTokenSource = new CancellationTokenSource();
            _hostTask = Task.CompletedTask;
        }
        public void Start()
        {
            _hostTask = Task.Run(() => _scriptHostManager.Initialize(_cancellationTokenSource.Token));
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();

            _hostTask.Wait(TimeSpan.FromSeconds(10));
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
