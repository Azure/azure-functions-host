// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// This StorageProvider provides a wrapper to align all uses of storage blobs by the Functions Host.
    /// The Host overrides the base AzureStorageProvider in Microsoft.Azure.WebJobs.Host.Storage to provide the IConfiguration that
    /// unifies the WebHost and inner ScriptHost configurations. This unification is done through ActiveHostConfigurationSource.
    /// </summary>
    internal class HostAzureStorageProvider : AzureStorageProvider
    {
        private ILogger _logger;

        public HostAzureStorageProvider(IScriptHostManager scriptHostManager, IConfiguration configuration, BlobServiceClientProvider blobServiceClientProvider, IOptionsMonitor<JobHostInternalStorageOptions> options, ILogger<AzureStorageProvider> logger) : base(configuration, blobServiceClientProvider, options)
        {
            _logger = logger;

            if (FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableMergedWebHostScriptHostConfiguration))
            {
                Configuration = configuration;
            }
            else
            {
                _ = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
                Configuration = new ConfigurationBuilder()
                    .Add(new ActiveHostConfigurationSource(scriptHostManager))
                    .AddConfiguration(configuration)
                    .Build();
            }
        }
    }
}
