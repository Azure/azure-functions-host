// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
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
        private readonly StartupContextProvider _startupContextProvider;
        private readonly IAzureBlobStorageProvider _azureBlobStorageProvider;
        private Lazy<ISecretManager> _secretManagerLazy;

        public DefaultSecretManagerProvider(IOptionsMonitor<ScriptApplicationHostOptions> options, IHostIdProvider hostIdProvider,
            IConfiguration configuration, IEnvironment environment, ILoggerFactory loggerFactory, IMetricsLogger metricsLogger, HostNameProvider hostNameProvider, StartupContextProvider startupContextProvider, IAzureBlobStorageProvider azureBlobStorageProvider)
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
            _startupContextProvider = startupContextProvider ?? throw new ArgumentNullException(nameof(startupContextProvider));

            _loggerFactory = loggerFactory;
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _secretManagerLazy = new Lazy<ISecretManager>(Create);

            // When these options change (due to specialization), we need to reset the secret manager.
            options.OnChange(_ => ResetSecretManager());

            _azureBlobStorageProvider = azureBlobStorageProvider ?? throw new ArgumentNullException(nameof(azureBlobStorageProvider));
        }

        public ISecretManager Current => _secretManagerLazy.Value;

        private void ResetSecretManager() => Interlocked.Exchange(ref _secretManagerLazy, new Lazy<ISecretManager>(Create));

        private ISecretManager Create() => new SecretManager(CreateSecretsRepository(), _loggerFactory.CreateLogger<SecretManager>(), _metricsLogger, _hostNameProvider, _startupContextProvider);

        internal ISecretsRepository CreateSecretsRepository()
        {
            ISecretsRepository repository;

            string secretStorageType = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            string secretStorageSas = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageSas);

            if (secretStorageType != null && secretStorageType.Equals(FileStorage, StringComparison.OrdinalIgnoreCase))
            {
                repository = new FileSystemSecretsRepository(_options.CurrentValue.SecretsPath, _loggerFactory.CreateLogger<FileSystemSecretsRepository>(), _environment);
            }
            else if (secretStorageType != null && secretStorageType.Equals("keyvault", StringComparison.OrdinalIgnoreCase))
            {
                string azureWebJobsSecretStorageKeyVaultUri = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultUri);
                string azureWebJobsSecretStorageKeyVaultClientId = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultClientId);
                string azureWebJobsSecretStorageKeyVaultClientSecret = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultClientSecret);
                string azureWebJobsSecretStorageKeyVaultTenantId = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultTenantId);

                var keyVaultLogger = _loggerFactory.CreateLogger<KeyVaultSecretsRepository>();

                return new KeyVaultSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"), azureWebJobsSecretStorageKeyVaultUri, azureWebJobsSecretStorageKeyVaultClientId,
                                                                    azureWebJobsSecretStorageKeyVaultClientSecret, azureWebJobsSecretStorageKeyVaultTenantId, keyVaultLogger, _environment);
            }
            else if (secretStorageType != null && secretStorageType.Equals("kubernetes", StringComparison.OrdinalIgnoreCase))
            {
                repository = new KubernetesSecretsRepository(_environment, new SimpleKubernetesClient(_environment, _loggerFactory.CreateLogger<SimpleKubernetesClient>()));
            }
            else if (secretStorageSas != null)
            {
                string siteSlotName = _environment.GetAzureWebsiteUniqueSlotName() ?? _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                repository = new BlobStorageSasSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"),
                                                                 secretStorageSas,
                                                                 siteSlotName,
                                                                 _loggerFactory.CreateLogger<BlobStorageSasSecretsRepository>(),
                                                                 _environment,
                                                                 _azureBlobStorageProvider);
            }
            else if (_azureBlobStorageProvider.TryCreateHostingBlobContainerClient(out _))
            {
                string siteSlotName = _environment.GetAzureWebsiteUniqueSlotName() ?? _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                repository = new BlobStorageSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"),
                                                              ConnectionStringNames.Storage,
                                                              siteSlotName,
                                                              _loggerFactory.CreateLogger<BlobStorageSecretsRepository>(),
                                                              _environment,
                                                              _azureBlobStorageProvider);
            }
            else
            {
                throw new InvalidOperationException($"Secret initialization from Blob storage failed due to missing both an Azure Storage connection string and a SAS connection uri. " +
                    $"For Blob Storage, please provide at least one of these. If you intend to use files for secrets, add an App Setting key '{EnvironmentSettingNames.AzureWebJobsSecretStorageType}' with value '{FileStorage}'.");
            }

            ILogger logger = _loggerFactory.CreateLogger<DefaultSecretManagerProvider>();
            logger.LogInformation("Resolved secret storage provider {provider}", repository.Name);
            return repository;
        }
    }
}