// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultSecretsRepositoryFactory : ISecretsRepositoryFactory
    {
        private readonly IOptions<ScriptApplicationHostOptions> _webHostOptions;
        private readonly IOptions<ScriptJobHostOptions> _scriptHostOptions;
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;
        private readonly ILogger<DefaultSecretsRepositoryFactory> _logger;

        public DefaultSecretsRepositoryFactory(IOptions<ScriptApplicationHostOptions> webHostOptions,
            IOptions<ScriptJobHostOptions> scriptHostOptions,
            IConfiguration configuration,
            IEnvironment environment,
            ILogger<DefaultSecretsRepositoryFactory> logger)
        {
            _webHostOptions = webHostOptions ?? throw new ArgumentNullException(nameof(webHostOptions));
            _scriptHostOptions = scriptHostOptions ?? throw new ArgumentNullException(nameof(scriptHostOptions));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ISecretsRepository Create()
        {
            string secretStorageType = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            string storageString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
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
