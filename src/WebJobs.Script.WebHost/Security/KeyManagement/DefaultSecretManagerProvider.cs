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
using DataProtectionConstants = Microsoft.Azure.Web.DataProtection.Constants;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultSecretManagerProvider : ISecretManagerProvider
    {
        private const string FileStorage = "Files";
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<DefaultSecretManagerProvider> _logger;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IEnvironment _environment;
        private readonly HostNameProvider _hostNameProvider;
        private readonly StartupContextProvider _startupContextProvider;
        private readonly IAzureBlobStorageProvider _azureBlobStorageProvider;

        /// <summary>
        /// Provides the managed file-secret encryption key configuration.
        /// </summary>
        private readonly IConfiguration _configuration;
        private Lazy<ISecretManager> _secretManagerLazy;
        private Lazy<bool> _secretsEnabledLazy;

        /// <summary>
        /// Initializes the provider using environment-backed key configuration.
        /// </summary>
        /// <param name="options">The host options.</param>
        /// <param name="hostIdProvider">The host identifier provider.</param>
        /// <param name="environment">The hosting environment.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="metricsLogger">The metrics logger.</param>
        /// <param name="hostNameProvider">The host name provider.</param>
        /// <param name="startupContextProvider">The startup context provider.</param>
        /// <param name="azureBlobStorageProvider">The blob storage provider.</param>
        public DefaultSecretManagerProvider(IOptionsMonitor<ScriptApplicationHostOptions> options, IHostIdProvider hostIdProvider, IEnvironment environment,
            ILoggerFactory loggerFactory, IMetricsLogger metricsLogger, HostNameProvider hostNameProvider, StartupContextProvider startupContextProvider,
            IAzureBlobStorageProvider azureBlobStorageProvider)
            : this(options, hostIdProvider, environment, loggerFactory, metricsLogger, hostNameProvider, startupContextProvider,
                  azureBlobStorageProvider, new ConfigurationBuilder().AddEnvironmentVariables().Build())
        {
        }

        /// <summary>
        /// Initializes the provider with configuration for managed Logic App file encryption.
        /// </summary>
        /// <param name="options">The host options.</param>
        /// <param name="hostIdProvider">The host identifier provider.</param>
        /// <param name="environment">The hosting environment.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="metricsLogger">The metrics logger.</param>
        /// <param name="hostNameProvider">The host name provider.</param>
        /// <param name="startupContextProvider">The startup context provider.</param>
        /// <param name="azureBlobStorageProvider">The blob storage provider.</param>
        /// <param name="configuration">The host configuration.</param>
        public DefaultSecretManagerProvider(IOptionsMonitor<ScriptApplicationHostOptions> options, IHostIdProvider hostIdProvider, IEnvironment environment,
            ILoggerFactory loggerFactory, IMetricsLogger metricsLogger, HostNameProvider hostNameProvider, StartupContextProvider startupContextProvider,
            IAzureBlobStorageProvider azureBlobStorageProvider, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _options = options ?? throw new ArgumentNullException(nameof(options));
            _hostIdProvider = hostIdProvider ?? throw new ArgumentNullException(nameof(hostIdProvider));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _hostNameProvider = hostNameProvider ?? throw new ArgumentNullException(nameof(hostNameProvider));
            _startupContextProvider = startupContextProvider ?? throw new ArgumentNullException(nameof(startupContextProvider));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<DefaultSecretManagerProvider>();

            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _secretManagerLazy = new Lazy<ISecretManager>(Create);
            _secretsEnabledLazy = new Lazy<bool>(GetSecretsEnabled);

            // When these options change (due to specialization), we need to reset the secret manager.
            options.OnChange(_ => ResetSecretManager());

            _azureBlobStorageProvider = azureBlobStorageProvider ?? throw new ArgumentNullException(nameof(azureBlobStorageProvider));
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

            _logger.LogDebug(new EventId(1, "ResetSecretManager"), "Reset SecretManager.");
        }

        /// <summary>
        /// Creates a secret manager with explicit key-repository activation only for managed Logic App files.
        /// </summary>
        private ISecretManager Create()
        {
            var repository = CreateSecretsRepository();
            var useAzureKeyRepository = repository is FileSystemSecretsRepository &&
                _environment.IsManagedAppEnvironment() && _environment.IsLogicApp();
            var converterFactory = new DefaultKeyValueConverterFactory(
                repositorySupportsEncryption: repository.IsEncryptionSupported,
                useAzureKeyRepository: useAzureKeyRepository);
            return new SecretManager(repository, converterFactory, _loggerFactory.CreateLogger<SecretManager>(), _metricsLogger, _hostNameProvider, _startupContextProvider);
        }

        internal ISecretsRepository CreateSecretsRepository()
        {
            ISecretsRepository repository = null;

            if (TryGetSecretsRepositoryType(out Type repositoryType))
            {
                if (repositoryType == typeof(FileSystemSecretsRepository))
                {
                    if (_environment.IsManagedAppEnvironment() && _environment.IsLogicApp())
                    {
                        ValidateManagedFileEncryptionKey();
                    }

                    repository = new FileSystemSecretsRepository(_options.CurrentValue.SecretsPath, _loggerFactory.CreateLogger<FileSystemSecretsRepository>(), _environment);
                }
                else if (repositoryType == typeof(KeyVaultSecretsRepository))
                {
                    string azureWebJobsSecretStorageKeyVaultUri = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultUri);
                    string azureWebJobsSecretStorageKeyVaultClientId = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultClientId);
                    string azureWebJobsSecretStorageKeyVaultClientSecret = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultClientSecret);
                    string azureWebJobsSecretStorageKeyVaultTenantId = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultTenantId);

                    var keyVaultLogger = _loggerFactory.CreateLogger<KeyVaultSecretsRepository>();

                    return new KeyVaultSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"), azureWebJobsSecretStorageKeyVaultUri, azureWebJobsSecretStorageKeyVaultClientId,
                                                                        azureWebJobsSecretStorageKeyVaultClientSecret, azureWebJobsSecretStorageKeyVaultTenantId, keyVaultLogger, _environment);
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
                                                                        _azureBlobStorageProvider);
                }
                else if (repositoryType == typeof(BlobStorageSecretsRepository))
                {
                    string siteSlotName = _environment.GetAzureWebsiteUniqueSlotName() ?? _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                    repository = new BlobStorageSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"),
                                                                    ConnectionStringNames.Storage,
                                                                    siteSlotName,
                                                                    _loggerFactory.CreateLogger<BlobStorageSecretsRepository>(),
                                                                    _environment,
                                                                    _azureBlobStorageProvider);
                }
                else if (repositoryType == typeof(ContainerAppsSecretsRepository))
                {
                    repository = new ContainerAppsSecretsRepository(_loggerFactory.CreateLogger<ContainerAppsSecretsRepository>());
                }
            }

            if (repository == null)
            {
                throw new InvalidOperationException("Secret initialization from Blob storage failed due to missing both an Azure Storage connection string and a SAS connection uri. " +
                        $"For Blob Storage, please provide at least one of these. If you intend to use files for secrets, add an App Setting key '{EnvironmentSettingNames.AzureWebJobsSecretStorageType}' with value '{FileStorage}'.");
            }

            _logger.LogInformation(new EventId(3, "CreatedSecretRespository"), "Resolved secret storage provider {provider}", repository.Name);

            return repository;
        }

        /// <summary>
        /// Rejects unusable encryption material before managed Logic App file secrets can be persisted.
        /// </summary>
        private void ValidateManagedFileEncryptionKey()
        {
            var encryptionKey = _configuration[DataProtectionConstants.AzureWebsiteLocalEncryptionKey];
            if (string.IsNullOrEmpty(encryptionKey) || encryptionKey.Length != 64)
            {
                throw new InvalidOperationException("Managed file secret storage requires a 256-bit hexadecimal 'AzureWebEncryptionKey'.");
            }

            foreach (var keyCharacter in encryptionKey)
            {
                if (!Uri.IsHexDigit(keyCharacter))
                {
                    throw new InvalidOperationException("Managed file secret storage requires a 256-bit hexadecimal 'AzureWebEncryptionKey'.");
                }
            }
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
            string secretStorageType = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
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
            else if (secretStorageType != null && secretStorageType.Equals("containerapps", StringComparison.OrdinalIgnoreCase))
            {
                repositoryType = typeof(ContainerAppsSecretsRepository);
                return true;
            }
            else if (secretStorageSas != null)
            {
                repositoryType = typeof(BlobStorageSasSecretsRepository);
                return true;
            }
            else if (_azureBlobStorageProvider.TryCreateHostingBlobContainerClient(out _))
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
            bool secretsEnabled = TryGetSecretsRepositoryType(out Type repositoryType);

            _logger.LogDebug(new EventId(2, "GetSecretsEnabled"), "SecretsEnabled evaluated to {secretsEnabled} with type {provider}.", secretsEnabled, repositoryType?.Name);

            return secretsEnabled;
        }
    }
}