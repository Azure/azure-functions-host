// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultSecretsRepositoryFactory : ISecretsRepositoryFactory
    {
        private readonly IOptions<ScriptWebHostOptions> _webHostOptions;
        private readonly IOptions<ScriptHostOptions> _scriptHostOptions;
        private readonly IConnectionStringProvider _connectionStringProvider;
        private readonly IEnvironment _environment;
        private readonly ILogger<DefaultSecretsRepositoryFactory> _logger;

        public DefaultSecretsRepositoryFactory(IOptions<ScriptWebHostOptions> webHostOptions,
            IOptions<ScriptHostOptions> scriptHostOptions,
            IConnectionStringProvider connectionStringProvider,
            IEnvironment environment,
            ILogger<DefaultSecretsRepositoryFactory> logger)
        {
            _webHostOptions = webHostOptions ?? throw new ArgumentNullException(nameof(webHostOptions));
            _scriptHostOptions = scriptHostOptions ?? throw new ArgumentNullException(nameof(scriptHostOptions));
            _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ISecretsRepository Create()
        {
            string secretStorageType = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            string storageString = _connectionStringProvider.GetConnectionString(ConnectionStringNames.Storage);
            if (secretStorageType != null && secretStorageType.Equals("Blob", StringComparison.OrdinalIgnoreCase) && storageString != null)
            {
                // TODO: DI (FACAVAL) Review
                string siteSlotName = _environment.GetEnvironmentVariable(_environment.GetAzureWebsiteUniqueSlotName()) ?? "testid"; //config.HostConfig.HostId;
                return new BlobStorageSecretsMigrationRepository(Path.Combine(_webHostOptions.Value.SecretsPath, "Sentinels"), storageString, siteSlotName, _logger);
            }
            else
            {
                return new FileSystemSecretsRepository(_webHostOptions.Value.SecretsPath);
            }
        }
    }
}
