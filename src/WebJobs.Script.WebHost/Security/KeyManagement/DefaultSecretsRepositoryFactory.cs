﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultSecretsRepositoryFactory : ISecretsRepositoryFactory
    {
        public ISecretsRepository Create(ScriptSettingsManager settingsManager, WebHostSettings webHostSettings, ScriptHostConfiguration config, IConnectionStringProvider connectionStringProvider)
        {
            string secretStorageType = settingsManager.GetSetting(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            string storageString = connectionStringProvider.GetConnectionString(ConnectionStringNames.Storage);
            if (secretStorageType != null && secretStorageType.Equals("Blob", StringComparison.OrdinalIgnoreCase) && storageString != null)
            {
                string siteSlotName = settingsManager.AzureWebsiteUniqueSlotName ?? config.HostConfig.HostId;
                return new BlobStorageSecretsMigrationRepository(Path.Combine(webHostSettings.SecretsPath, "Sentinels"), storageString, siteSlotName, logger);
            }
            else
            {
                return new FileSystemSecretsRepository(webHostSettings.SecretsPath);
            }
        }
    }
}
