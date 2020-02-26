// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    internal class ExternalConfigurationStartupValidatorService : IHostedService
    {
        private readonly ExternalConfigurationStartupValidator _validator;
        private readonly IConfigurationRoot _originalConfig;
        private readonly ILogger<ExternalConfigurationStartupValidator> _logger;

        public ExternalConfigurationStartupValidatorService(ExternalConfigurationStartupValidator validator, IConfigurationRoot originalConfig, ILogger<ExternalConfigurationStartupValidator> logger)
        {
            _validator = validator;
            _originalConfig = originalConfig;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                IDictionary<string, IEnumerable<string>> invalidValues = _validator.Validate(_originalConfig);

                if (invalidValues.Any())
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("The Functions scale controller may not scale the following functions correctly because some configuration values were modified in an external startup class.");

                    foreach (KeyValuePair<string, IEnumerable<string>> invalidValueMap in invalidValues)
                    {
                        sb.AppendLine($"  Function '{invalidValueMap.Key}' uses the modified key(s): {string.Join(", ", invalidValueMap.Value)}");
                    }

                    _logger.LogWarning(new EventId(700, "ConfigurationModifiedInExternalStartup"), sb.ToString());
                }
            }
            catch (Exception ex)
            {
                // We don't want to fail on this, but log to make sure we can improve.
                _logger.LogError(new EventId(701, "ExternalConfigurationStartupValidationError"), ex, "Error while validating external startup configuration.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private class KnownTrigger
        {
            public KnownTrigger(string connectionSettingName, string defaultConnectionSetting)
            {
                ConnectionSettingName = connectionSettingName;
                DefaultConnectionSetting = defaultConnectionSetting;
            }

            /// <summary>
            /// Gets the default connection key ("AzureWebJobsStorage", "AzureWebJobsCosmosDb") for the binding.
            /// </summary>
            public string DefaultConnectionSetting { get; }

            /// <summary>
            /// Gets the connection key ("connection", "connectionStringSetting", etc) for the binding.
            /// </summary>
            public string ConnectionSettingName { get; }
        }
    }
}
