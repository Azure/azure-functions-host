// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Health
{
    internal class TokenExpirationService : IHostedService, IDisposable
    {
        private readonly TaskCompletionSource<object> _standby = new();
        private readonly IEnvironment _environment;
        private readonly ILogger<TokenExpirationService> _logger;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptionsMonitor;
        private IDisposable _listener;
        private bool _analysisScheduled;
        private Task _analysisTask;
        private bool _disposed;
        private CancellationTokenSource _cancellationTokenSource;

        // The TokenExpirationService is a good health check candidate if we start using Microsoft.Extensions.Diagnostics.HealthChecks
        public TokenExpirationService(IEnvironment environment, ILogger<TokenExpirationService> logger, IOptionsMonitor<StandbyOptions> standbyOptionsMonitor)
        {
            _environment = environment;
            _logger = logger;
            _standbyOptionsMonitor = standbyOptionsMonitor;
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
                                ScheduleTokenExpirationServiceAnalysis();
                            }
                        });
                    }
                    else
                    {
                        ScheduleTokenExpirationServiceAnalysis();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting Token expiration service. Handling error and continuing.");
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

        private void ScheduleTokenExpirationServiceAnalysis()
        {
            _analysisScheduled = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _analysisTask = Task.Delay(TimeSpan.FromMinutes(1), _cancellationTokenSource.Token)
               .ContinueWith(t => AnalyzeSasTokenInUri());
        }

        private void AnalyzeSasTokenInUri()
        {
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            // If AzureWebJobsStorage__accountName is set, we are using identities and don't need to check for SAS token expiration
            if (!string.IsNullOrEmpty(_environment.GetEnvironmentVariable($"{EnvironmentSettingNames.AzureWebJobsSecretStorage}__accountName")))
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
            if (!string.IsNullOrEmpty(azureWebJobsStorage) && azureWebJobsStorage.StartsWith("BlobEndpoint=", StringComparison.OrdinalIgnoreCase)
                && azureWebJobsStorage.Contains("SharedAccessSignature=", StringComparison.OrdinalIgnoreCase))
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
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    var isAzureWebJobsStorage = setting.Key == EnvironmentSettingNames.AzureWebJobsSecretStorage;
                    string sasTokenExpirationDate = Utility.GetSasTokenExpirationDate(uri, isAzureWebJobsStorage);

                    if (string.IsNullOrEmpty(sasTokenExpirationDate))
                    {
                        continue;
                    }

                    DateTime.TryParse(sasTokenExpirationDate, out var parsedDate);

                    var difference = parsedDate.Subtract(currentDate);

                    // Log an error event if the token is already expired; otherwise log a warning event
                    if (difference.TotalDays <= 0)
                    {
                        string message = string.Format(Resources.SasTokenExpiredFormat, setting.Key);
                        DiagnosticEventLoggerExtensions.LogDiagnosticEventError(_logger, message, DiagnosticEventConstants.SasTokenExpiringErrorCode, DiagnosticEventConstants.SasTokenExpiringErrorHelpLink, new Exception(message));
                    }
                    else if (difference.TotalDays <= 45)
                    {
                        string message = string.Format(Resources.SasTokenExpiringFormat, (int)difference.TotalDays, setting.Key);
                        DiagnosticEventLoggerExtensions.LogDiagnosticEvent(_logger, Microsoft.Extensions.Logging.LogLevel.Warning, 0, DiagnosticEventConstants.SasTokenExpiringErrorCode, message, DiagnosticEventConstants.SasTokenExpiringErrorHelpLink, exception: null);
                    }
                    else
                    {
                        string message = string.Format(Resources.SasTokenExpiringInfoFormat, (int)difference.TotalDays, setting.Key);
                        DiagnosticEventLoggerExtensions.LogDiagnosticEventInformation(_logger, DiagnosticEventConstants.SasTokenExpiringErrorCode, message, DiagnosticEventConstants.SasTokenExpiringErrorHelpLink);
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
