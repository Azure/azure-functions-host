// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    internal class ExternalConfigurationStartupValidator : IHostedService
    {
        private readonly IConfigurationRoot _originalConfig;
        private readonly IConfiguration _config;
        private readonly IFunctionMetadataManager _metadataManager;
        private readonly ILogger<ExternalConfigurationStartupValidator> _logger;

        private static readonly IDictionary<string, KnownTrigger> _knownTriggers = new Dictionary<string, KnownTrigger>
        {
            { "blobTrigger", new KnownTrigger("connection", "AzureWebJobsStorage") },
            { "queueTrigger", new KnownTrigger("connection", "AzureWebJobsStorage") },
            { "cosmosDBTrigger", new KnownTrigger("connectionStringSetting", "ConnectionStrings:CosmosDB") }
            // TODO: the rest
        };

        public ExternalConfigurationStartupValidator(IConfigurationRoot originalConfig, IConfiguration config, IFunctionMetadataManager metadataManager, ILogger<ExternalConfigurationStartupValidator> logger)
        {
            _originalConfig = originalConfig;
            _config = config;
            _metadataManager = metadataManager;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var functions = _metadataManager.Functions;

            foreach (var function in functions)
            {
                var trigger = function.Bindings.SingleOrDefault(b => b.IsTrigger);
                if (_knownTriggers.TryGetValue(trigger?.Type, out KnownTrigger details))
                {
                    // If connection is used, you cannot override it.
                    string connection = trigger.Raw[details.ConnectionSettingName]?.ToString() ?? details.DefaultConnectionSetting;

                    if (!string.IsNullOrEmpty(connection))
                    {
                        string originalKey = _originalConfig[connection];
                        string key = _config[connection];
                        if (originalKey != key)
                        {
                            _logger.LogWarning($"The configuration value '{connection}' was changed in an external startup class. The Functions scale controller may not scale the function '{function.Name}' correctly.");
                        }
                    }
                }
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
