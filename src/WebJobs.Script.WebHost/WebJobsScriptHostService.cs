// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebJobsScriptHostService : IHostedService, IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly FunctionsServiceProvider _serviceProvider;
        private readonly ILogger _logger;
        private bool _disposed = false;
        private Task _hostTask;
        private IHost _host;

        public WebJobsScriptHostService(FunctionsServiceProvider serviceProvider, ILoggerFactory loggerFactory)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _hostTask = Task.CompletedTask;
            _serviceProvider = serviceProvider;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing Azure Functions Host.");
            _host = BuildHost();
            _hostTask = _host.StartAsync(cancellationToken);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource.Cancel();

            Task result = await Task.WhenAny(_host.StopAsync(cancellationToken), Task.Delay(TimeSpan.FromSeconds(10)));

            if (result != _hostTask)
            {
                _logger.LogWarning("Script host manager did not shutdown within its allotted time.");
            }
            else
            {
                _logger.LogInformation("Script host manager shutdown completed.");
            }
        }

        private IHost BuildHost()
        {
            return new HostBuilder()
                            .UseServiceProviderFactory(new ExternalFunctionsServiceProviderFactory(_serviceProvider))
                            .ConfigureServices(s =>
                            {
                                var fa = new LoggerFactory();
                                fa.AddConsole(LogLevel.Warning);
                                s.AddSingleton<ILoggerFactory>(fa);
                                s.AddSingleton<IHostLifetime, ScriptHostLifetime>();
                                s.AddSingleton<WebJobs.Host.Executors.IHostIdProvider, IdProvider>();
                            })
                            .ConfigureAppConfiguration(c =>
                            {
                                c.Add(new WebScriptHostConfigurationSource());
                            })
                            .AddScriptHostServices()
                            .ConfigureWebJobsHost()
                            .AddWebJobsLogging() // Enables WebJobs v1 classic logging
                            .AddAzureStorageCoreServices()
                            .AddAzureStorage()
                            .AddHttp()
                            .ConfigureServices(s =>
                            {
                                s.RemoveAll<IHostedService>();
                                s.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobHostService>());
                                s.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HttpInitializationService>());
                            })
                            .Build();
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

    public class IdProvider : WebJobs.Host.Executors.IHostIdProvider
    {
        public IdProvider()
        {
        }

        public Task<string> GetHostIdAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("0980980980980980980980989009");
        }
    }
}
