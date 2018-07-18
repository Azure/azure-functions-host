// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultSecretsRepositoryFactory : ISecretsRepositoryFactory
    {
        private readonly IOptions<ScriptWebHostOptions> _webHostOptions;
        private readonly IOptions<ScriptHostOptions> _scriptHostOptions;
        private readonly IConnectionStringProvider _connectionStringProvider;

        public DefaultSecretsRepositoryFactory(IOptions<ScriptWebHostOptions> webHostOptions,
            IOptions<ScriptHostOptions> scriptHostOptions,
            IConnectionStringProvider connectionStringProvider)
        {
            _webHostOptions = webHostOptions ?? throw new ArgumentNullException(nameof(webHostOptions));
            _scriptHostOptions = scriptHostOptions ?? throw new ArgumentNullException(nameof(scriptHostOptions));
            _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        }

        public ISecretsRepository Create()
        {
            string secretStorageType = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            string storageString = _connectionStringProvider.GetConnectionString(ConnectionStringNames.Storage);
            if (secretStorageType != null && secretStorageType.Equals("Blob", StringComparison.OrdinalIgnoreCase) && storageString != null)
            {
                string siteSlotName = settingsManager.AzureWebsiteUniqueSlotName ?? config.HostConfig.HostId;
                return new BlobStorageSecretsMigrationRepository(Path.Combine(webHostSettings.SecretsPath, "Sentinels"), storageString, siteSlotName, logger);
            }
            else
            {
                return new FileSystemSecretsRepository(_webHostOptions.Value.SecretsPath);
            }
        }
    }
}
