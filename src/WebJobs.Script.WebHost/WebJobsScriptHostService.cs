﻿// Copyright (c) .NET Foundation. All rights reserved.
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
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebJobsScriptHostService : IHostedService, IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly IServiceProvider _rootServiceProvider;
        private readonly IServiceScopeFactory _rootScopeFactory;
        private readonly IOptions<ScriptWebHostOptions> _webHostOptions;
        private readonly ILogger _logger;
        private bool _disposed = false;
        private Task _hostTask;
        private IHost _host;

        public WebJobsScriptHostService(IOptions<ScriptWebHostOptions> webHostOptions, IServiceProvider rootServiceProvider, IServiceScopeFactory rootScopeFactory, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));
            _rootScopeFactory = rootScopeFactory ?? throw new ArgumentNullException(nameof(rootScopeFactory));
            _webHostOptions = webHostOptions ?? throw new ArgumentNullException(nameof(webHostOptions));

            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

            _hostTask = Task.CompletedTask;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public IServiceProvider Services => _host?.Services;

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
            var builder = new HostBuilder();

            // Host configuration
            builder.UseServiceProviderFactory(new ScriptHostScopedServiceProviderFactory(_rootServiceProvider, _rootScopeFactory))
                            .ConfigureServices(s =>
                            {
                                // TODO: DI (FACAVAL) Temporary - replace with proper logger factory using
                                // job host configuration
                                var fa = new LoggerFactory();
                                fa.AddConsole(LogLevel.Trace);
                                s.AddSingleton<ILoggerFactory>(fa);
                                s.AddSingleton<IHostLifetime, ScriptHostLifetime>();
                            })
                            .ConfigureAppConfiguration(c =>
                            {
                                c.Add(new HostJsonFileConfigurationSource(_webHostOptions));
                            });

            // WebJobs configuration
            builder.AddScriptHost(_webHostOptions);

            // HACK: Remove previous IHostedService registration
            // TODO: DI (FACAVAL) Remove this and move HttpInitialization to webjobs configuration
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<IHostedService>();
                s.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobHostService>());
                s.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HttpInitializationService>());
            });

            return builder.Build();
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
