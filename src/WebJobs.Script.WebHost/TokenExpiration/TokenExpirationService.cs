// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.TokenExpiration
{
    internal class TokenExpirationService : BackgroundService, IDisposable
    {
        private readonly TaskCompletionSource<object> _standby = new();
        private readonly IEnvironment _environment;
        private readonly ILogger<TokenExpirationService> _logger;
        private IDisposable _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _analysisTask;
        private bool _analysisScheduled;

        public TokenExpirationService(IEnvironment environment, ILogger<TokenExpirationService> logger, IOptionsMonitor<StandbyOptions> standbyOptionsMonitor)
        {
            _environment = environment;
            _logger = logger;
            if (standbyOptionsMonitor.CurrentValue.InStandbyMode)
            {
                _listener = standbyOptionsMonitor.OnChange(standbyOptions =>
                {
                    if (!standbyOptions.InStandbyMode && !_analysisScheduled)
                    {
                        _standby.TrySetResult(null);
                    }
                });
            }
            else
            {
                _standby.TrySetResult(null);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await Task.Yield(); // force a yield, so our synchronous code does not slowdown startup
            await _standby.Task.WaitAsync(cancellationToken);

            if (_environment.IsCoreTools())
            {
                return;
            }

            // Execute first during startup
            ScheduleTokenExpirationCheck();
            while (!cancellationToken.IsCancellationRequested)
            {
                // check at every hour
                await Task.Delay(TimeSpan.FromHours(1), cancellationToken)
               .ContinueWith(t => ScheduleTokenExpirationCheck());
            }

            // Dispose of the listener for the standby options monitor
            _listener?.Dispose();
        }

        private void ScheduleTokenExpirationCheck()
        {
            _analysisScheduled = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _analysisTask = Task.Delay(TimeSpan.FromSeconds(1), _cancellationTokenSource.Token)
               .ContinueWith(t => AnalyzeSasTokenInUri());
        }

        private void AnalyzeSasTokenInUri()
        {
            var accountNameEnvVar = $"{_environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorage)}__accountName";
            // If AzureWebJobsStorage__accountName is set, we are using identities and don't need to check for SAS token expiration
            if (!string.IsNullOrEmpty(_environment.GetEnvironmentVariable(accountNameEnvVar)))
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
    }
}
