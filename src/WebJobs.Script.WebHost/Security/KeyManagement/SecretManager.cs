// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using static Microsoft.Azure.WebJobs.Script.WebHost.Models.FunctionAppSecrets;
using DataProtectionConstants = Microsoft.Azure.Web.DataProtection.Constants;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class SecretManager : IDisposable, ISecretManager
    {
        private readonly IKeyValueConverterFactory _keyValueConverterFactory;
        private readonly ILogger _logger;
        private readonly ISecretsRepository _repository;
        private readonly HostNameProvider _hostNameProvider;
        private readonly StartupContextProvider _startupContextProvider;
        private readonly IHostMetrics _hostMetrics;
        private readonly Lazy<bool> _strictHISFeatureEnabled = new Lazy<bool>(() => FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagStrictHISModeEnabled));
        private readonly Lazy<bool> _strictHISWarnFeatureEnabled = new Lazy<bool>(() => FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagStrictHISModeWarn));
        private readonly HashSet<string> _invalidNonHISKeys = new HashSet<string>();
        private ConcurrentDictionary<string, IDictionary<string, string>> _functionSecrets;
        private ConcurrentDictionary<string, (string, AuthorizationLevel)> _authorizationCache = new ConcurrentDictionary<string, (string, AuthorizationLevel)>(StringComparer.OrdinalIgnoreCase);
        private HostSecretsInfo _hostSecrets;
        private SemaphoreSlim _hostSecretsLock = new SemaphoreSlim(1, 1);
        private ConcurrentDictionary<string, SemaphoreSlim> _functionSecretsLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private IMetricsLogger _metricsLogger;
        private string _repositoryClassName;
        private DateTime _lastCacheResetTime;

        public SecretManager()
        {
        }

        public SecretManager(ISecretsRepository repository, ILogger logger, IMetricsLogger metricsLogger, IHostMetrics hostMetrics, HostNameProvider hostNameProvider, StartupContextProvider startupContextProvider)
            : this(repository, new DefaultKeyValueConverterFactory(repository.IsEncryptionSupported), logger, metricsLogger, hostMetrics, hostNameProvider, startupContextProvider)
        {
        }

        public SecretManager(ISecretsRepository repository, IKeyValueConverterFactory keyValueConverterFactory, ILogger logger, IMetricsLogger metricsLogger, IHostMetrics hostMetrics, HostNameProvider hostNameProvider, StartupContextProvider startupContextProvider)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _keyValueConverterFactory = keyValueConverterFactory ?? throw new ArgumentNullException(nameof(keyValueConverterFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _hostMetrics = hostMetrics ?? throw new ArgumentNullException(nameof(hostMetrics));
            _hostNameProvider = hostNameProvider ?? throw new ArgumentNullException(nameof(hostNameProvider));
            _startupContextProvider = startupContextProvider ?? throw new ArgumentNullException(nameof(startupContextProvider));

            _repositoryClassName = _repository.GetType().Name.ToLower();
            _repository.SecretsChanged += OnSecretsChanged;

            InitializeCache();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                (_repository as IDisposable)?.Dispose();
                _hostSecretsLock.Dispose();
            }
        }

        public async virtual Task<HostSecretsInfo> GetHostSecretsAsync()
        {
            if (_hostSecrets == null)
            {
                using (_metricsLogger.LatencyEvent(GetMetricEventName(MetricEventNames.SecretManagerGetHostSecrets)))
                {
                    await _hostSecretsLock.WaitAsync();

                    HostSecrets hostSecrets;
                    try
                    {
                        _logger.LogDebug("Loading host secrets");

                        hostSecrets = await LoadSecretsAsync<HostSecrets>();
                        try
                        {
                            if (hostSecrets == null)
                            {
                                // host secrets do not yet exist so generate them
                                _logger.LogDebug(Resources.TraceHostSecretGeneration);
                                hostSecrets = GenerateHostSecrets();
                                await PersistSecretsAsync(hostSecrets);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Exception while generating host secrets. This can happen if another instance is also generating secrets. Attempting to read secrets again.");
                            hostSecrets = await LoadSecretsAsync<HostSecrets>();

                            if (hostSecrets == null)
                            {
                                _logger.LogError("Host secrets are still null on second attempt.");
                                throw;
                            }
                        }

                        try
                        {
                            // Host secrets will be in the original persisted state at this point (e.g. encrypted),
                            // so we read the secrets running them through the appropriate readers
                            hostSecrets = ReadHostSecrets(hostSecrets);
                        }
                        catch (CryptographicException ex) when (!ex.InnerException.IsFatal())
                        {
                            string message = string.Format(Resources.TraceNonDecryptedHostSecretRefresh, ex);
                            _logger.LogDebug(message);
                            await PersistSecretsAsync(hostSecrets, null, true);
                            hostSecrets = GenerateHostSecrets(hostSecrets);
                            await RefreshSecretsAsync(hostSecrets);
                        }

                        // If the persistence state of any of our secrets is stale (e.g. the encryption key has been rotated), update
                        // the state and persist the secrets
                        if (hostSecrets.HasStaleKeys)
                        {
                            _logger.LogDebug(Resources.TraceStaleHostSecretRefresh);
                            await RefreshSecretsAsync(hostSecrets);
                        }

                        // before caching  any secrets, validate them
                        string masterKeyValue = hostSecrets.MasterKey.Value;
                        var functionKeys = hostSecrets.FunctionKeys.ToDictionary(p => p.Name, p => p.Value);
                        var systemKeys = hostSecrets.SystemKeys.ToDictionary(p => p.Name, p => p.Value);
                        ValidateHostSecrets(masterKeyValue, functionKeys, systemKeys);

                        _hostSecrets = new HostSecretsInfo
                        {
                            MasterKey = masterKeyValue,
                            FunctionKeys = functionKeys,
                            SystemKeys = systemKeys
                        };
                    }
                    finally
                    {
                        _hostSecretsLock.Release();
                    }
                }
            }

            return _hostSecrets;
        }

        public async virtual Task<IDictionary<string, string>> GetFunctionSecretsAsync(string functionName, bool merged = false)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                throw new ArgumentNullException(nameof(functionName));
            }

            functionName = functionName.ToLowerInvariant();
            if (!_functionSecrets.TryGetValue(functionName, out IDictionary<string, string> functionSecrets))
            {
                using (_metricsLogger.LatencyEvent(GetMetricEventName(MetricEventNames.SecretManagerGetFunctionSecrets), functionName))
                {
                    var functionSecretsLock = GetFunctionSecretsLock(functionName);
                    await functionSecretsLock.WaitAsync();

                    try
                    {
                        _logger.LogDebug($"Loading secrets for function '{functionName}'");

                        FunctionSecrets secrets = await LoadFunctionSecretsAsync(functionName);

                        try
                        {
                            if (secrets == null)
                            {
                                // no secrets exist for this function so generate them
                                string message = string.Format(Resources.TraceFunctionSecretGeneration, functionName);
                                _logger.LogDebug(message);
                                secrets = GenerateFunctionSecrets();

                                await PersistSecretsAsync(secrets, functionName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Exception while generating function secrets. This can happen if another instance is also generating secrets. Attempting to read secrets again.");
                            secrets = await LoadFunctionSecretsAsync(functionName);

                            if (secrets == null)
                            {
                                _logger.LogError("Function secrets are still null on second attempt.");
                                throw;
                            }
                        }

                        try
                        {
                            // Read all secrets, which will run the keys through the appropriate readers
                            secrets.Keys = secrets.Keys.Select(k => _keyValueConverterFactory.ReadKey(k)).ToList();
                        }
                        catch (CryptographicException ex) when (!ex.InnerException.IsFatal())
                        {
                            string message = string.Format(Resources.TraceNonDecryptedFunctionSecretRefresh, functionName, ex);
                            _logger.LogDebug(message);
                            await PersistSecretsAsync(secrets, functionName, true);
                            secrets = GenerateFunctionSecrets(secrets);
                            await RefreshSecretsAsync(secrets, functionName);
                        }

                        if (secrets.HasStaleKeys)
                        {
                            _logger.LogDebug(string.Format(Resources.TraceStaleFunctionSecretRefresh, functionName));
                            await RefreshSecretsAsync(secrets, functionName);
                        }

                        // before caching any secrets, validate them
                        var result = secrets.Keys.ToDictionary(s => s.Name, s => s.Value);
                        ValidateSecrets(result, SecretGenerator.FunctionKeySeed, functionName);

                        functionSecrets = _functionSecrets.AddOrUpdate(functionName, result, (n, r) => result);
                    }
                    finally
                    {
                        functionSecretsLock.Release();
                    }
                }
            }

            if (merged)
            {
                // If merged is true, we combine function specific keys with host level function keys,
                // prioritizing function specific keys
                var hostSecrets = await GetHostSecretsAsync();
                functionSecrets = functionSecrets.Union(hostSecrets.FunctionKeys.Where(s => !functionSecrets.ContainsKey(s.Key)))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            return functionSecrets;
        }

        public async Task<KeyOperationResult> AddOrUpdateFunctionSecretAsync(string secretName, string secret, string keyScope, ScriptSecretsType secretsType)
        {
            using (_metricsLogger.LatencyEvent(GetMetricEventName(MetricEventNames.SecretManagerAddOrUpdateFunctionSecret), GetFunctionName(keyScope, secretsType)))
            {
                Func<ScriptSecrets> secretsFactory = null;

                if (secretsType == ScriptSecretsType.Function)
                {
                    secretsFactory = () => new FunctionSecrets(new List<Key>());
                }
                else if (secretsType == ScriptSecretsType.Host)
                {
                    secretsType = ScriptSecretsType.Host;
                    secretsFactory = GenerateHostSecrets;
                }
                else
                {
                    throw new NotSupportedException($"Secrets type {secretsType.ToString("G")} not supported.");
                }

                KeyOperationResult result = await AddOrUpdateSecretAsync(secretsType, keyScope, secretName, secret, secretsFactory);

                _logger.LogInformation(string.Format(Resources.TraceAddOrUpdateFunctionSecret, secretsType, secretName, keyScope ?? "host", result.Result));

                return result;
            }
        }

        public async Task<KeyOperationResult> SetMasterKeyAsync(string value = null)
        {
            using (_metricsLogger.LatencyEvent(GetMetricEventName(MetricEventNames.SecretManagerSetMasterKey)))
            {
                HostSecrets secrets = await LoadSecretsAsync<HostSecrets>();

                if (secrets == null)
                {
                    secrets = GenerateHostSecrets();
                }

                OperationResult result;
                string masterKey;
                if (value == null)
                {
                    // Generate a new secret (clear)
                    masterKey = SecretGenerator.GenerateMasterKeyValue();
                    result = OperationResult.Created;
                }
                else
                {
                    if (_strictHISFeatureEnabled.Value)
                    {
                        // if an explicit value has been provided and strict HIS mode is enabled, validate the secret
                        if (!SecretGenerator.TryValidateSecret(value, SecretGenerator.MasterKeySeed))
                        {
                            return new KeyOperationResult(value, OperationResult.BadRequest);
                        }
                    }

                    // Use the provided secret
                    masterKey = value;
                    result = OperationResult.Updated;
                }

                // Creates a key with the new master key (which will be encrypted, if required)
                secrets.MasterKey = CreateKey(ScriptConstants.DefaultMasterKeyName, masterKey);

                await PersistSecretsAsync(secrets);

                _logger.LogInformation(string.Format(Resources.TraceMasterKeyCreatedOrUpdated, result));

                return new KeyOperationResult(masterKey, result);
            }
        }

        public async Task<bool> DeleteSecretAsync(string secretName, string keyScope, ScriptSecretsType secretsType)
        {
            using (_metricsLogger.LatencyEvent(GetMetricEventName(MetricEventNames.SecretManagerDeleteSecret), GetFunctionName(keyScope, secretsType)))
            {
                bool deleted = await ModifyFunctionSecretAsync(secretsType, keyScope, secretName, (secrets, key) =>
                {
                    secrets?.RemoveKey(key, keyScope);
                    return secrets;
                });

                if (deleted)
                {
                    string target = secretsType == ScriptSecretsType.Function
                        ? $"Function ('{keyScope}')"
                        : $"Host (scope: '{keyScope}')";

                    _logger.LogInformation(string.Format(Resources.TraceSecretDeleted, target, secretName));
                }

                return deleted;
            }
        }

        internal static ulong GetKeySeed(ScriptSecretsType secretsType, string keyScope)
        {
            if (secretsType == ScriptSecretsType.Function)
            {
                return SecretGenerator.FunctionKeySeed;
            }
            else if (secretsType == ScriptSecretsType.Host)
            {
                if (string.Equals(keyScope, HostKeyScopes.FunctionKeys, StringComparison.OrdinalIgnoreCase))
                {
                    return SecretGenerator.FunctionKeySeed;
                }
                else if (string.Equals(keyScope, HostKeyScopes.SystemKeys, StringComparison.OrdinalIgnoreCase))
                {
                    return SecretGenerator.SystemKeySeed;
                }
            }

            return 0;
        }

        public static string GetKeyType(ulong seed)
        {
            switch (seed)
            {
                case SecretGenerator.MasterKeySeed:
                    return "Master";
                case SecretGenerator.SystemKeySeed:
                    return "System";
                case SecretGenerator.FunctionKeySeed:
                    return "Function";
                default:
                    return "Unknown";
            }
        }

        private async Task<KeyOperationResult> AddOrUpdateSecretAsync(ScriptSecretsType secretsType, string keyScope,
            string secretName, string secret, Func<ScriptSecrets> secretsFactory)
        {
            OperationResult result = OperationResult.NotFound;

            if (!string.IsNullOrEmpty(secret) && _strictHISFeatureEnabled.Value)
            {
                // if an explicit value has been provided and strict HIS mode is enabled, validate the secret
                ulong seed = GetKeySeed(secretsType, keyScope);
                if (seed > 0 && !SecretGenerator.TryValidateSecret(secret, seed))
                {
                    return new KeyOperationResult(secret, OperationResult.BadRequest);
                }
            }

            secret ??= SecretGenerator.GenerateFunctionKeyValue();

            await ModifyFunctionSecretsAsync(secretsType, keyScope, secrets =>
            {
                Key key = secrets.GetFunctionKey(secretName, keyScope);

                var createAndUpdateKey = new Action<OperationResult>((o) =>
                {
                    var newKey = CreateKey(secretName, secret);
                    secrets.AddKey(newKey, keyScope);
                    result = o;
                });

                if (key == null)
                {
                    createAndUpdateKey(OperationResult.Created);
                }
                else if (secrets.RemoveKey(key, keyScope))
                {
                    createAndUpdateKey(OperationResult.Updated);
                }

                return secrets;
            }, secretsFactory);

            return new KeyOperationResult(secret, result);
        }

        private async Task<bool> ModifyFunctionSecretAsync(ScriptSecretsType secretsType, string keyScope, string secretName, Func<ScriptSecrets, Key, ScriptSecrets> keyChangeHandler, Func<ScriptSecrets> secretFactory = null)
        {
            bool secretFound = false;

            await ModifyFunctionSecretsAsync(secretsType, keyScope, secrets =>
            {
                Key key = secrets?.GetFunctionKey(secretName, keyScope);

                if (key != null)
                {
                    secretFound = true;

                    secrets = keyChangeHandler(secrets, key);
                }

                return secrets;
            }, secretFactory);

            return secretFound;
        }

        private async Task ModifyFunctionSecretsAsync(ScriptSecretsType secretsType, string keyScope, Func<ScriptSecrets, ScriptSecrets> changeHandler, Func<ScriptSecrets> secretFactory)
        {
            ScriptSecrets currentSecrets = await LoadSecretsAsync(secretsType, keyScope);

            if (currentSecrets == null)
            {
                currentSecrets = secretFactory?.Invoke();
            }

            var newSecrets = changeHandler(currentSecrets);

            if (newSecrets != null)
            {
                await PersistSecretsAsync(newSecrets, keyScope);
            }
        }

        private Task<FunctionSecrets> LoadFunctionSecretsAsync(string functionName) => LoadSecretsAsync<FunctionSecrets>(functionName);

        private async Task<T> LoadSecretsAsync<T>(string keyScope = null) where T : ScriptSecrets
        {
            ScriptSecretsType type = GetSecretsType<T>();

            var result = await LoadSecretsAsync(type, keyScope);

            return result as T;
        }

        private async Task<ScriptSecrets> LoadSecretsAsync(ScriptSecretsType type, string keyScope)
        {
            return await _repository.ReadAsync(type, keyScope);
        }

        public async Task<(string KeyName, AuthorizationLevel Level)> GetAuthorizationLevelOrNullAsync(string keyValue, string functionName = null)
        {
            // local helper function to get auth level and also enforce HIS validation
            async Task<(string KeyName, AuthorizationLevel Level)> GetAuthorizationLevelAndValidateAsync(string keyValue, string functionName)
            {
                var result = await GetAuthorizationLevelAsync(this, keyValue, functionName);

                if (result.Level != AuthorizationLevel.Anonymous &&
                    _strictHISFeatureEnabled.Value && _invalidNonHISKeys.Contains(keyValue))
                {
                    // HIS strict mode is enabled and the matched key is invalid
                    // Such keys cannot grant access.
                    return (null, AuthorizationLevel.Anonymous);
                }

                return result;
            }

            if (keyValue != null)
            {
                string cacheKey = $"{keyValue}{functionName}";
                if (_authorizationCache.TryGetValue(cacheKey, out (string, AuthorizationLevel) value))
                {
                    // we've already authorized this key value so return the cached result
                    return value;
                }

                // Before authorizing, check the cache load state. Do this first because checking the auth level will
                // cause the secrets to be loaded into cache - we want to know if they were cached BEFORE this check.
                bool secretsCached = _hostSecrets != null || _functionSecrets.Any();

                var result = await GetAuthorizationLevelAndValidateAsync(keyValue, functionName);
                if (result.Level != AuthorizationLevel.Anonymous)
                {
                    // key match
                    _authorizationCache[cacheKey] = result;
                    return result;
                }
                else
                {
                    // A key was presented but there wasn't a match. If we used cached key values,
                    // reset cache and try once more.
                    // We throttle resets, to ensure invalid requests can't force us to slam storage.
                    if (secretsCached && ((DateTime.UtcNow - _lastCacheResetTime) > TimeSpan.FromMinutes(1)))
                    {
                        _hostSecrets = null;
                        _functionSecrets.Clear();
                        _invalidNonHISKeys.Clear();
                        _lastCacheResetTime = DateTime.UtcNow;

                        return await GetAuthorizationLevelAndValidateAsync(keyValue, functionName);
                    }
                }
            }

            // no key match
            return (null, AuthorizationLevel.Anonymous);
        }

        internal static async Task<(string KeyName, AuthorizationLevel Level)> GetAuthorizationLevelAsync(ISecretManager secretManager, string keyValue, string functionName = null)
        {
            // see if the key specified is the master key
            HostSecretsInfo hostSecrets = await secretManager.GetHostSecretsAsync();
            if (!string.IsNullOrEmpty(hostSecrets.MasterKey) &&
                Key.SecretValueEquals(keyValue, hostSecrets.MasterKey))
            {
                return (ScriptConstants.DefaultMasterKeyName, AuthorizationLevel.Admin);
            }

            if (HasMatchingKey(hostSecrets.SystemKeys, keyValue, out string keyName))
            {
                return (keyName, AuthorizationLevel.System);
            }

            // see if the key specified matches the host function key
            if (HasMatchingKey(hostSecrets.FunctionKeys, keyValue, out keyName))
            {
                return (keyName, AuthorizationLevel.Function);
            }

            // If there is a function specific key specified try to match against that
            if (functionName != null)
            {
                IDictionary<string, string> functionSecrets = await secretManager.GetFunctionSecretsAsync(functionName);
                if (HasMatchingKey(functionSecrets, keyValue, out keyName))
                {
                    return (keyName, AuthorizationLevel.Function);
                }
            }

            return (null, AuthorizationLevel.Anonymous);
        }

        private static bool HasMatchingKey(IDictionary<string, string> secrets, string keyValue, out string matchedKeyName)
        {
            matchedKeyName = null;
            if (secrets == null)
            {
                return false;
            }

            string matchedValue;
            (matchedKeyName, matchedValue) = secrets.FirstOrDefault(s => Key.SecretValueEquals(s.Value, keyValue));

            return matchedValue != null;
        }

        private static ScriptSecretsType GetSecretsType<T>() where T : ScriptSecrets
        {
            return typeof(HostSecrets).IsAssignableFrom(typeof(T))
                ? ScriptSecretsType.Host
                : ScriptSecretsType.Function;
        }

        private void ValidateHostSecrets(string masterKey, IDictionary<string, string> functionKeys, IDictionary<string, string> systemKeys)
        {
            ValidateSecret(ScriptConstants.MasterKeyName, masterKey, SecretGenerator.MasterKeySeed);
            ValidateSecrets(functionKeys, SecretGenerator.FunctionKeySeed);
            ValidateSecrets(systemKeys, SecretGenerator.SystemKeySeed);
        }

        private void ValidateSecret(string keyName, string keyValue, ulong seed, string functionName = null)
        {
            if (SecretGenerator.TryValidateSecret(keyValue, seed))
            {
                _metricsLogger.LogEvent(MetricEventNames.IdentifiableSecretLoaded, functionName);
            }
            else
            {
                _metricsLogger.LogEvent(MetricEventNames.NonIdentifiableSecretLoaded, functionName);

                if (_strictHISFeatureEnabled.Value || _strictHISWarnFeatureEnabled.Value)
                {
                    // if either HIS mode is enabled log the appropriate diagnostic event
                    // and track the secret as invalid
                    string message = string.Format(Resources.NonHISSecret, GetKeyType(seed), keyName, functionName);
                    LogLevel level = _strictHISFeatureEnabled.Value ? LogLevel.Error : LogLevel.Warning;
                    _logger.LogDiagnosticEvent(level, 0, DiagnosticEventConstants.NonHISSecretLoaded, message, DiagnosticEventConstants.NonHISSecretLoadedHelpLink, exception: null);

                    _invalidNonHISKeys.Add(keyValue);
                }
            }
        }

        private void ValidateSecrets(IDictionary<string, string> keys, ulong seed, string functionName = null)
        {
            if (keys != null)
            {
                foreach (var key in keys)
                {
                    ValidateSecret(key.Key, key.Value, seed, functionName);
                }
            }
        }

        private HostSecrets GenerateHostSecrets()
        {
            return new HostSecrets
            {
                MasterKey = GenerateMasterKey(),
                FunctionKeys = new List<Key>
                {
                    GenerateFunctionKey(),
                },
                SystemKeys = new List<Key>()
            };
        }

        private HostSecrets GenerateHostSecrets(HostSecrets secrets)
        {
            if (secrets.MasterKey.IsEncrypted)
            {
                secrets.MasterKey.Value = SecretGenerator.GenerateMasterKeyValue();
            }
            secrets.SystemKeys = RegenerateKeys(secrets.SystemKeys, SecretGenerator.SystemKeySeed);
            secrets.FunctionKeys = RegenerateKeys(secrets.FunctionKeys, SecretGenerator.FunctionKeySeed);
            return secrets;
        }

        private FunctionSecrets GenerateFunctionSecrets()
        {
            return new FunctionSecrets
            {
                Keys = new List<Key>
                {
                    GenerateFunctionKey()
                }
            };
        }

        private FunctionSecrets GenerateFunctionSecrets(FunctionSecrets secrets)
        {
            secrets.Keys = RegenerateKeys(secrets.Keys, SecretGenerator.FunctionKeySeed);
            return secrets;
        }

        private IList<Key> RegenerateKeys(IList<Key> list, ulong seed)
        {
            return list.Select(k =>
            {
                if (k.IsEncrypted)
                {
                    k.Value = SecretGenerator.GenerateIdentifiableSecret(seed);
                }
                return k;
            }).ToList();
        }

        private Task RefreshSecretsAsync<T>(T secrets, string keyScope = null) where T : ScriptSecrets
        {
            var refreshedSecrets = secrets.Refresh(_keyValueConverterFactory);

            return PersistSecretsAsync(refreshedSecrets, keyScope);
        }

        private async Task PersistSecretsAsync<T>(T secrets, string keyScope = null, bool isNonDecryptable = false) where T : ScriptSecrets
        {
            if (secrets != null)
            {
                secrets.HostName = _hostNameProvider.Value;
            }

            ScriptSecretsType secretsType = secrets.SecretsType;
            if (isNonDecryptable)
            {
                string[] secretBackups = await _repository.GetSecretSnapshots(secrets.SecretsType, keyScope);

                if (secretBackups.Length >= ScriptConstants.MaximumSecretBackupCount)
                {
                    string message = string.Format(Resources.ErrorTooManySecretBackups, ScriptConstants.MaximumSecretBackupCount, string.IsNullOrEmpty(keyScope) ? "host" : keyScope, await AnalyzeSnapshots(secretBackups));
                    _logger?.LogDebug(message);

                    var exception = new InvalidOperationException(message);
                    _logger?.LogDiagnosticEventError(DiagnosticEventConstants.MaximumSecretBackupCountErrorCode, message, DiagnosticEventConstants.MaximumSecretBackupCountHelpLink, exception);

                    _hostMetrics.AppFailure();

                    throw exception;
                }
                await _repository.WriteSnapshotAsync(secretsType, keyScope, secrets);
            }
            else
            {
                // We want to store encryption keys hashes to investigate sudden regenerations
                string hashes = GetEncryptionKeysHashes();
                secrets.DecryptionKeyId = hashes;
                _logger?.LogInformation("Encryption keys hashes: {0}", hashes);

                await _repository.WriteAsync(secretsType, keyScope, secrets);
            }

            // do a direct/immediate cache update to avoid race conditions with
            // file based notifications.
            ClearCacheOnChange(secrets.SecretsType, keyScope);
        }

        private HostSecrets ReadHostSecrets(HostSecrets hostSecrets)
        {
            return new HostSecrets
            {
                MasterKey = _keyValueConverterFactory.ReadKey(hostSecrets.MasterKey),
                FunctionKeys = hostSecrets.FunctionKeys.Select(k => _keyValueConverterFactory.ReadKey(k)).ToList(),
                SystemKeys = hostSecrets.SystemKeys?.Select(k => _keyValueConverterFactory.ReadKey(k)).ToList() ?? new List<Key>()
            };
        }

        private Key GenerateMasterKey()
        {
            string secret = SecretGenerator.GenerateMasterKeyValue();

            return CreateKey(ScriptConstants.DefaultMasterKeyName, secret);
        }

        private Key GenerateFunctionKey()
        {
            string secret = SecretGenerator.GenerateFunctionKeyValue();

            return CreateKey(ScriptConstants.DefaultFunctionKeyName, secret);
        }

        private Key CreateKey(string name, ulong seed)
        {
            string secret = SecretGenerator.GenerateIdentifiableSecret(seed);

            return CreateKey(name, secret);
        }

        private Key CreateKey(string name, string secret)
        {
            var key = new Key(name, secret);

            return _keyValueConverterFactory.WriteKey(key);
        }

        private void OnSecretsChanged(object sender, SecretsChangedEventArgs e)
        {
            ClearCacheOnChange(e.SecretsType, e.Name);
        }

        private void ClearCacheOnChange(ScriptSecretsType secretsType, string functionName)
        {
            // clear the cached secrets if they exist
            // they'll be reloaded on demand next time
            _authorizationCache.Clear();

            if (secretsType == ScriptSecretsType.Host && _hostSecrets != null)
            {
                _logger.LogInformation("Host keys change detected. Clearing cache.");
                _hostSecrets = null;
            }
            else
            {
                if (!string.IsNullOrEmpty(functionName) && _functionSecrets.ContainsKey(functionName))
                {
                    _logger.LogInformation($"Function keys change detected. Clearing cache for function '{functionName}'.");
                    _functionSecrets.TryRemove(functionName, out _);
                }
                else if (_functionSecrets.Any())
                {
                    _logger.LogInformation("Function keys change detected. Clearing all cached function keys.");
                    _functionSecrets.Clear();
                }
            }
        }

        public void ClearCache()
        {
            _authorizationCache.Clear();
            _hostSecrets = null;
            _functionSecrets.Clear();
            _invalidNonHISKeys.Clear();
        }

        private async Task<string> AnalyzeSnapshots(string[] secretBackups)
        {
            string analyzeResult = string.Empty;
            try
            {
                List<ScriptSecrets> snapShots = new List<ScriptSecrets>();
                foreach (string secretPath in secretBackups)
                {
                    ScriptSecrets secrets = await _repository.ReadAsync(ScriptSecretsType.Function, Path.GetFileNameWithoutExtension(secretPath));
                    snapShots.Add(secrets);
                }
                string[] hosts = snapShots.Select(x => x.HostName).Distinct().ToArray();
                if (hosts.Length > 1)
                {
                    analyzeResult = string.Format(Resources.ErrorSameSecrets, string.Join(",", hosts));
                }
            }
            catch
            {
                // best effort
            }
            return analyzeResult;
        }

        public async Task PurgeOldSecretsAsync(string rootScriptPath, ILogger logger)
        {
            using (_metricsLogger.LatencyEvent(GetMetricEventName(MetricEventNames.SecretManagerPurgeOldSecrets)))
            {
                if (!Directory.Exists(rootScriptPath))
                {
                    return;
                }

                // Create a lookup of all potential functions (whether they're valid or not)
                // It is important that we determine functions based on the presence of a folder,
                // not whether we've identified a valid function from that folder. This ensures
                // that we don't delete logs/secrets for functions that transition into/out of
                // invalid unparsable states.
                var currentFunctions = Directory.EnumerateDirectories(rootScriptPath).Select(p => Path.GetFileName(p)).ToList();

                await _repository.PurgeOldSecretsAsync(currentFunctions, logger);
            }
        }

        private string GetMetricEventName(string name)
        {
            return string.Format(CultureInfo.InvariantCulture, name, _repositoryClassName);
        }

        private string GetFunctionName(string keyScope, ScriptSecretsType secretsType)
        {
            return (secretsType == ScriptSecretsType.Function) ? keyScope : null;
        }

        private void InitializeCache()
        {
            var cachedFunctionSecrets = _startupContextProvider.GetFunctionSecretsOrNull();
            if (cachedFunctionSecrets != null)
            {
                foreach (var functionKeys in cachedFunctionSecrets)
                {
                    ValidateSecrets(functionKeys.Value, SecretGenerator.FunctionKeySeed, functionKeys.Key);
                }

                _functionSecrets = new ConcurrentDictionary<string, IDictionary<string, string>>(cachedFunctionSecrets, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _functionSecrets = new ConcurrentDictionary<string, IDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }

            var hostSecrets = _startupContextProvider.GetHostSecretsOrNull();
            if (hostSecrets != null)
            {
                ValidateHostSecrets(hostSecrets.MasterKey, hostSecrets.FunctionKeys, hostSecrets.SystemKeys);
                _hostSecrets = hostSecrets;
            }
        }

        private string GetEncryptionKeysHashes()
        {
            string result = string.Empty;
            string azureWebsiteLocalEncryptionKey = SystemEnvironment.Instance.GetEnvironmentVariable(DataProtectionConstants.AzureWebsiteLocalEncryptionKey) ?? string.Empty;
            SHA256 hash = SHA256.Create();

            if (!string.IsNullOrEmpty(azureWebsiteLocalEncryptionKey))
            {
                byte[] hashBytes = hash.ComputeHash(Encoding.UTF8.GetBytes(azureWebsiteLocalEncryptionKey));
                string azureWebsiteLocalEncryptionKeyHash = Convert.ToBase64String(hashBytes);
                result += $"{DataProtectionConstants.AzureWebsiteLocalEncryptionKey}={azureWebsiteLocalEncryptionKeyHash};";
            }

            string azureWebsiteEnvironmentMachineKey = SystemEnvironment.Instance.GetEnvironmentVariable(DataProtectionConstants.AzureWebsiteEnvironmentMachineKey) ?? string.Empty;
            if (!string.IsNullOrEmpty(azureWebsiteEnvironmentMachineKey))
            {
                byte[] hashBytes = hash.ComputeHash(Encoding.UTF8.GetBytes(azureWebsiteEnvironmentMachineKey));
                string azureWebsiteEnvironmentMachineKeyHash = Convert.ToBase64String(hashBytes);
                result += $"{DataProtectionConstants.AzureWebsiteEnvironmentMachineKey}={azureWebsiteEnvironmentMachineKeyHash};";
            }

            return result;
        }

        private SemaphoreSlim GetFunctionSecretsLock(string functionName)
        {
            // We're only serializing access to secrets per-function, not across all functions,
            // so we need to ensure we're using a single shared lock per-function.
            return _functionSecretsLocks.GetOrAdd(functionName, static k => new SemaphoreSlim(1, 1));
        }
    }
}