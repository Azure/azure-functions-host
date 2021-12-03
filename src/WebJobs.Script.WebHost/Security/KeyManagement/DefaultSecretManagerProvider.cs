// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Extensions;
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
        private readonly IAzureStorageProvider _azureStorageProvider;
        private Lazy<ISecretManager> _secretManagerLazy;
        private Lazy<bool> _secretsEnabledLazy;

        public DefaultSecretManagerProvider(IOptionsMonitor<ScriptApplicationHostOptions> options, IHostIdProvider hostIdProvider,
            IConfiguration configuration, IEnvironment environment, ILoggerFactory loggerFactory, IMetricsLogger metricsLogger, HostNameProvider hostNameProvider, StartupContextProvider startupContextProvider, IAzureStorageProvider azureStorageProvider)
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
            _secretsEnabledLazy = new Lazy<bool>(GetSecretsEnabled);

            // When these options change (due to specialization), we need to reset the secret manager.
            options.OnChange(_ => ResetSecretManager());

            _azureStorageProvider = azureStorageProvider;
        }

        public bool SecretsEnabled
        {
            get
            {
                if (_secretManagerLazy.IsValueCreated)
                {
                    return true;
                }
                return _secretsEnabledLazy.Value;
            }
        }

        public ISecretManager Current => _secretManagerLazy.Value;

        private void ResetSecretManager()
        {
            Interlocked.Exchange(ref _secretsEnabledLazy, new Lazy<bool>(GetSecretsEnabled));
            Interlocked.Exchange(ref _secretManagerLazy, new Lazy<ISecretManager>(Create));
        }

        private ISecretManager Create() => new SecretManager(CreateSecretsRepository(), _loggerFactory.CreateLogger<SecretManager>(), _metricsLogger, _hostNameProvider, _startupContextProvider);

        internal ISecretsRepository CreateSecretsRepository()
        {
            ISecretsRepository repository = null;

            if (TryGetSecretsRepositoryType(out Type repositoryType))
            {
                if (repositoryType == typeof(FileSystemSecretsRepository))
                {
                    repository = new FileSystemSecretsRepository(_options.CurrentValue.SecretsPath, _loggerFactory.CreateLogger<FileSystemSecretsRepository>(), _environment);
                }
                else if (repositoryType == typeof(KeyVaultSecretsRepository))
                {
                    string azureWebJobsSecretStorageKeyVaultName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultName);
                    string azureWebJobsSecretStorageKeyVaultConnectionString = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultConnectionString);

                    repository = new KeyVaultSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"),
                                                               azureWebJobsSecretStorageKeyVaultName,
                                                               azureWebJobsSecretStorageKeyVaultConnectionString,
                                                               _loggerFactory.CreateLogger<KeyVaultSecretsRepository>(),
                                                               _environment);
                }
                else if (repositoryType == typeof(KubernetesSecretsRepository))
                {
                    repository = new KubernetesSecretsRepository(_environment, new SimpleKubernetesClient(_environment, _loggerFactory.CreateLogger<SimpleKubernetesClient>()));
                }
                else if (repositoryType == typeof(BlobStorageSasSecretsRepository))
                {
                    string secretStorageSas = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageSas);
                    string siteSlotName = _environment.GetAzureWebsiteUniqueSlotName() ?? _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                    repository = new BlobStorageSasSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"),
                                                                     secretStorageSas,
                                                                     siteSlotName,
                                                                     _loggerFactory.CreateLogger<BlobStorageSasSecretsRepository>(),
                                                                     _environment,
                                                                     _azureStorageProvider);
                }
                else if (repositoryType == typeof(BlobStorageSecretsRepository))
                {
                    string siteSlotName = _environment.GetAzureWebsiteUniqueSlotName() ?? _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                    repository = new BlobStorageSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"),
                                                                  ConnectionStringNames.Storage,
                                                                  siteSlotName,
                                                                  _loggerFactory.CreateLogger<BlobStorageSecretsRepository>(),
                                                                  _environment,
                                                                  _azureStorageProvider);
                }
            }

            if (repository == null)
            {
                throw new InvalidOperationException($"Secret initialization from Blob storage failed due to missing both an Azure Storage connection string and a SAS connection uri. " +
                    $"For Blob Storage, please provide at least one of these. If you intend to use files for secrets, add an App Setting key '{EnvironmentSettingNames.AzureWebJobsSecretStorageType}' with value '{FileStorage}'.");
            }

            ILogger logger = _loggerFactory.CreateLogger<DefaultSecretManagerProvider>();
            logger.LogInformation("Resolved secret storage provider {provider}", repository.Name);

            return repository;
        }

        /// <summary>
        /// Determines the repository Type to use based on configured settings.
        /// </summary>
        /// <remarks>
        /// For scenarios where the app isn't configured for key storage (e.g. no AzureWebJobsSecretStorageType explicitly configured,
        /// no storage connection string for default blob storage, etc.). Note that it's still possible for the creation of the repository
        /// to fail due to invalid values. This method just does preliminary config checks to determine the Type.
        /// </remarks>
        /// <param name="repositoryType">The repository Type or null.</param>
        /// <returns>True if a Type was determined, false otherwise.</returns>
        internal bool TryGetSecretsRepositoryType(out Type repositoryType)
        {
            string secretStorageType = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            string secretStorageSas = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageSas);

            if (secretStorageType != null && secretStorageType.Equals(FileStorage, StringComparison.OrdinalIgnoreCase))
            {
                repositoryType = typeof(FileSystemSecretsRepository);
                return true;
            }
            else if (secretStorageType != null && secretStorageType.Equals("keyvault", StringComparison.OrdinalIgnoreCase))
            {
                repositoryType = typeof(KeyVaultSecretsRepository);
                return true;
            }
            else if (secretStorageType != null && secretStorageType.Equals("kubernetes", StringComparison.OrdinalIgnoreCase))
            {
                repositoryType = typeof(KubernetesSecretsRepository);
                return true;
            }
            else if (secretStorageSas != null)
            {
                repositoryType = typeof(BlobStorageSasSecretsRepository);
                return true;
            }
            else if (_azureStorageProvider.ConnectionExists(ConnectionStringNames.Storage))
            {
                repositoryType = typeof(BlobStorageSecretsRepository);
                return true;
            }
            else
            {
                repositoryType = null;
                return false;
            }
        }

        internal bool GetSecretsEnabled()
        {
            return TryGetSecretsRepositoryType(out _);
        }
    }
}