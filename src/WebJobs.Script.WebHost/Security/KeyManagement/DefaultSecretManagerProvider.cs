// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultSecretManagerProvider : ISecretManagerProvider
    {
        private const string FileStorage = "Files";
        private readonly ILoggerFactory _loggerFactory;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;
        private readonly HostNameProvider _hostNameProvider;
        private Lazy<ISecretManager> _secretManagerLazy;

        public DefaultSecretManagerProvider(IOptionsMonitor<ScriptApplicationHostOptions> options, IHostIdProvider hostIdProvider,
            IConfiguration configuration, IEnvironment environment, ILoggerFactory loggerFactory, IMetricsLogger metricsLogger, HostNameProvider hostNameProvider)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _options = options ?? throw new ArgumentNullException(nameof(options));
            _hostIdProvider = hostIdProvider ?? throw new ArgumentNullException(nameof(hostIdProvider));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _hostNameProvider = hostNameProvider ?? throw new ArgumentNullException(nameof(hostNameProvider));

            _loggerFactory = loggerFactory;
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _secretManagerLazy = new Lazy<ISecretManager>(Create);

            // When these options change (due to specialization), we need to reset the secret manager.
            options.OnChange(_ => ResetSecretManager());
        }

        public ISecretManager Current => _secretManagerLazy.Value;

        private void ResetSecretManager() => Interlocked.Exchange(ref _secretManagerLazy, new Lazy<ISecretManager>(Create));

        private ISecretManager Create() => new SecretManager(CreateSecretsRepository(), _loggerFactory.CreateLogger<SecretManager>(), _metricsLogger, _hostNameProvider);

        internal ISecretsRepository CreateSecretsRepository()
        {
            string secretStorageType = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            string storageString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
            string secretStorageSas = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageSas);
            if (secretStorageType != null && secretStorageType.Equals(FileStorage, StringComparison.OrdinalIgnoreCase))
            {
                return new FileSystemSecretsRepository(_options.CurrentValue.SecretsPath);
            }
            else if (secretStorageType != null && secretStorageType.Equals("keyvault", StringComparison.OrdinalIgnoreCase))
            {
                string azureWebJobsSecretStorageKeyVaultName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultName);
                string azureWebJobsSecretStorageKeyVaultConnectionString = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultConnectionString);
                return new KeyVaultSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"), azureWebJobsSecretStorageKeyVaultName, azureWebJobsSecretStorageKeyVaultConnectionString);
            }
            else if (secretStorageType != null && secretStorageType.Equals("kubernetes", StringComparison.OrdinalIgnoreCase))
            {
                return new KubernetesSecretsRepository(_environment, new SimpleKubernetesClient(_environment));
            }
            else if (secretStorageSas != null)
            {
                string siteSlotName = _environment.GetAzureWebsiteUniqueSlotName() ?? _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                return new BlobStorageSasSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"), secretStorageSas, siteSlotName, _loggerFactory.CreateLogger<BlobStorageSasSecretsRepository>());
            }
            else if (storageString != null)
            {
                string siteSlotName = _environment.GetAzureWebsiteUniqueSlotName() ?? _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                return new BlobStorageSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"), storageString, siteSlotName, _loggerFactory.CreateLogger<BlobStorageSecretsRepository>());
            }
            else
            {
                throw new InvalidOperationException($"Secret initialization from Blob storage failed due to missing both an Azure Storage connection string and a SAS connection uri. " +
                    $"For Blob Storage, please provide at least one of these. If you intend to use files for secrets, add an App Setting key '{EnvironmentSettingNames.AzureWebJobsSecretStorageType}' with value '{FileStorage}'.");
            }
        }
    }
}