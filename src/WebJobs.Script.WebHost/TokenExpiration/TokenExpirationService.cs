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
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Auth;
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
                                ScheduleTokenExpirationCheck();
                            }
                        });
                    }
                    else
                    {
                        ScheduleTokenExpirationCheck();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting token expiration service. Handling error and continuing.");
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
                    _logger.LogDebug("Token expiration service stopped before analysis completion. Waiting for cancellation.");

                    return _analysisTask;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Token expiration service. Handling error and continuing.");
            }
            return Task.CompletedTask;
        }

        private void ScheduleTokenExpirationCheck()
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

            // WEBSITE_RUN_FROM_PACKAGE and WEBSITE_CONTENTAZUREFILECONNECTIONSTRING
            Dictionary<string, string> appSettings = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteRunFromPackage, _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteRunFromPackage) },
                { EnvironmentSettingNames.AzureFilesConnectionString, _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureFilesConnectionString) }
            };

            // We only want to parse AzureWebJobsStorage in the following format
            // BlobEndpoint=<blob-endpoint-url>;QueueEndpoint=<queue-endpoint-url>;TableEndpoint=<table-endpoint-url>;SharedAccessSignature=<sas-token>
            var azureWebJobsStorage = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorage);

            // Check if the value starts with "BlobEndpoint=" and contains "SharedAccessSignature="
            if (azureWebJobsStorage.StartsWith("BlobEndpoint=", StringComparison.OrdinalIgnoreCase)
                && azureWebJobsStorage.Contains("SharedAccessSignature="))
            {
                appSettings.Add(EnvironmentSettingNames.AzureWebJobsSecretStorage, azureWebJobsStorage);
            }

            EmitLogging(appSettings);
        }

        private void EmitLogging(Dictionary<string, string> appSettings)
        {
            var currentDate = DateTime.Now.Date;
            foreach (var setting in appSettings)
            {
                var uri = setting.Value;
                if (!string.IsNullOrEmpty(uri))
                {
                    string sasTokenExpirationDate;

                    if (setting.Key == EnvironmentSettingNames.AzureWebJobsSecretStorage)
                    {
                        sasTokenExpirationDate = Utility.GetSasTokenExpirationDateFromSasSignature(uri);
                    }
                    else
                    {
                        sasTokenExpirationDate = Utility.GetSasTokenExpirationDate(new Uri(uri));
                    }

                    if (!string.IsNullOrEmpty(sasTokenExpirationDate))
                    {
                        var parsedDate = DateTime.Parse(sasTokenExpirationDate);

                        var difference = parsedDate.Subtract(currentDate);

                        // Log an error event if the token is already expired; otherwise log a warning event
                        if (difference.TotalDays <= 0)
                        {
                            string message = string.Format(Resources.SasTokenExpiredFormat, setting.Key);
                            DiagnosticEventLoggerExtensions.LogDiagnosticEventError(_logger, message, DiagnosticEventConstants.SasTokenExpiringErrorCode, DiagnosticEventConstants.SasTokenExpiringErrorHelpLink, new Exception(message));
                        }
                        if (difference.TotalDays <= 30)
                        {
                            string message = string.Format(Resources.SasTokenExpiringFormat, (int)difference.TotalDays, setting.Key);
                            DiagnosticEventLoggerExtensions.LogDiagnosticEvent(_logger, Microsoft.Extensions.Logging.LogLevel.Warning, 0, DiagnosticEventConstants.SasTokenExpiringErrorCode, message, DiagnosticEventConstants.SasTokenExpiringErrorHelpLink, exception: null);
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
