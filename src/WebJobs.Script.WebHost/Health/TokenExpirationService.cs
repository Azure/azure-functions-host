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

namespace Microsoft.Azure.WebJobs.Script.WebHost.Health
{
    internal class TokenExpirationService : BackgroundService
    {
        private readonly TaskCompletionSource<object> _standby = new();
        private readonly IEnvironment _environment;
        private readonly ILogger<TokenExpirationService> _logger;
        private IDisposable _listener;

        // The TokenExpirationService is a good health check candidate if we start using Microsoft.Extensions.Diagnostics.HealthChecks
        public TokenExpirationService(IEnvironment environment, ILogger<TokenExpirationService> logger, IOptionsMonitor<StandbyOptions> standbyOptionsMonitor)
        {
            _environment = environment;
            _logger = logger;
            if (standbyOptionsMonitor.CurrentValue.InStandbyMode)
            {
                _listener = standbyOptionsMonitor.OnChange(standbyOptions =>
                {
                    if (!standbyOptions.InStandbyMode)
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
            try
            {
                await Task.Yield(); // force a yield, so our synchronous code does not slowdown startup
                await _standby.Task.WaitAsync(cancellationToken);

                if (_environment.IsCoreTools())
                {
                    return;
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    // check at every hour
                    AnalyzeSasTokenInUri();
                    await Task.Delay(TimeSpan.FromHours(1), cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex.ToString());
            }
        }

        private void AnalyzeSasTokenInUri()
        {
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
                        DiagnosticEventLoggerExtensions.LogDiagnosticEventError(_logger, DiagnosticEventConstants.SasTokenExpiringErrorCode, message, DiagnosticEventConstants.SasTokenExpiringErrorHelpLink, new Exception(message));
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

        public override void Dispose()
        {
            base.Dispose();
            _listener?.Dispose();
        }
    }
}
