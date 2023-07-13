// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.TokenExpiration
{
    internal class TokenExpirationService : IHostedService, IDisposable
    {
        private readonly IEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptionsMonitor;
        private readonly WebJobsScriptHostService _scriptHost;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _analysisTask;
        private bool _disposed;
        private bool _analysisScheduled;
        private ILogger _logger;

        public TokenExpirationService(IEnvironment environment, WebJobsScriptHostService scriptHost, ILoggerFactory loggerFactory, IOptionsMonitor<StandbyOptions> standbyOptionsMonitor)
        {
            _environment = environment;
            _scriptHost = scriptHost;
            _loggerFactory = loggerFactory;
            _standbyOptionsMonitor = standbyOptionsMonitor;
            _logger = _loggerFactory.CreateLogger<TokenExpirationService>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_environment.IsCoreTools())
                {
                    if (_standbyOptionsMonitor.CurrentValue.InStandbyMode)
                    {
                        _standbyOptionsMonitor.OnChange(standbyOptions =>
                        {
                            if (!standbyOptions.InStandbyMode && !_analysisScheduled)
                            {
                                ScheduleAssemblyAnalysis();
                            }
                        });
                    }
                    else
                    {
                        ScheduleAssemblyAnalysis();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting Assembly analysis service. Handling error and continuing.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _cancellationTokenSource?.Cancel();

                if (_analysisTask != null && !_analysisTask.IsCompleted)
                {
                    _logger.LogDebug("Assembly analysis service stopped before analysis completion. Waiting for cancellation.");

                    return _analysisTask;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Assembly analysis service. Handling error and continuing.");
            }
            return Task.CompletedTask;
        }

        private void ScheduleAssemblyAnalysis()
        {
            _analysisScheduled = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _analysisTask = Task.Delay(TimeSpan.FromSeconds(5), _cancellationTokenSource.Token)
               .ContinueWith(t => AnalyzeSasTokenInUri());
        }

        private void AnalyzeSasTokenInUri()
        {
            var jobHost = _scriptHost.GetService<IScriptJobHost>();

            if (jobHost == null
                || _cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            // WEBSITE_RUN_FROM_PACKAGE, AzureWebJobsStorage, WEBSITE_CONTENTAZUREFILECONNECTIONSTRING
            string[] uris =
            {
                _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteRunFromPackage),
                _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureFilesConnectionString)
            };

            EmitLogging(uris);
        }

        private void EmitLogging(string[] uris)
        {
            foreach (var uri in uris)
            {
                if (!string.IsNullOrEmpty(uri))
                {
                    var sasTokenExpirationDate = Utility.GetSasTokenExpirationDate(new Uri(uri));
                    if (!string.IsNullOrEmpty(sasTokenExpirationDate))
                    {
                        var parsedDate = DateTime.Parse(sasTokenExpirationDate);
                        var currentDate = DateTime.Now.Date;

                        var difference = parsedDate.Subtract(currentDate);

                        if (Math.Abs(difference.TotalDays) <= 30)
                        {
                            string message = string.Format(Resources.SasTokenExpiringFormat, difference.TotalDays);
                            DiagnosticEventLoggerExtensions.LogDiagnosticEvent(_logger, LogLevel.Warning, 0, DiagnosticEventConstants.SasTokenExpiringErrorCode, message, DiagnosticEventConstants.SasTokenExpiringErrorHelpLink, null);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
    }
}
