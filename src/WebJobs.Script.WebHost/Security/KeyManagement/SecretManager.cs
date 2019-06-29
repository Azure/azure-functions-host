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
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class SecretManager : IDisposable, ISecretManager
    {
        private readonly ConcurrentDictionary<string, Dictionary<string, string>> _secretsMap = new ConcurrentDictionary<string, Dictionary<string, string>>();
        private readonly IKeyValueConverterFactory _keyValueConverterFactory;
        private readonly ILogger _logger;
        private readonly ISecretsRepository _repository;
        private readonly HostNameProvider _hostNameProvider;
        private HostSecretsInfo _hostSecrets;
        private SemaphoreSlim _hostSecretsLock = new SemaphoreSlim(1, 1);
        private IMetricsLogger _metricsLogger;
        private string _repositoryClassName;

        // for testing
        public SecretManager()
        {
        }

        public SecretManager(ISecretsRepository repository, ILogger logger, IMetricsLogger metricsLogger, HostNameProvider hostNameProvider, bool createHostSecretsIfMissing = false)
            : this(repository, new DefaultKeyValueConverterFactory(repository.IsEncryptionSupported), logger, metricsLogger, hostNameProvider, createHostSecretsIfMissing)
        {
        }

        public SecretManager(ISecretsRepository repository, IKeyValueConverterFactory keyValueConverterFactory, ILogger logger, IMetricsLogger metricsLogger, HostNameProvider hostNameProvider, bool createHostSecretsIfMissing = false)
        {
            _repository = repository;
            _keyValueConverterFactory = keyValueConverterFactory;
            _repository.SecretsChanged += OnSecretsChanged;
            _logger = logger;
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _repositoryClassName = _repository.GetType().Name.ToLower();
            _hostNameProvider = hostNameProvider;

            if (createHostSecretsIfMissing)
            {
                // GetHostSecrets will create host secrets if not present
                GetHostSecretsAsync().GetAwaiter().GetResult();
            }
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
            using (_metricsLogger.LatencyEvent(GetMetricEventName(MetricEventNames.SecretManagerGetHostSecrets)))
            {
                if (_hostSecrets == null)
                {
                    HostSecrets hostSecrets;
                    // Allow only one thread to modify the secrets
                    await _hostSecretsLock.WaitAsync();
                    try
                    {
                        hostSecrets = await LoadSecretsAsync<HostSecrets>();

                        if (hostSecrets == null)
                        {
                            // host secrets do not yet exist so generate them
                            _logger.LogDebug(Resources.TraceHostSecretGeneration);
                            hostSecrets = GenerateHostSecrets();
                            await PersistSecretsAsync(hostSecrets);
                        }

                        try
                        {
                            // Host secrets will be in the original persisted state at this point (e.g. encrypted),
                            // so we read the secrets running them through the appropriate readers
                            hostSecrets = ReadHostSecrets(hostSecrets);
                        }
                        catch (CryptographicException ex)
                        {
                            string message = string.Format(Resources.TraceNonDecryptedHostSecretRefresh, ex);
                            _logger?.LogDebug(message);
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

                        _hostSecrets = new HostSecretsInfo
                        {
                            MasterKey = hostSecrets.MasterKey.Value,
                            FunctionKeys = hostSecrets.FunctionKeys.ToDictionary(s => s.Name, s => s.Value),
                            SystemKeys = hostSecrets.SystemKeys.ToDictionary(s => s.Name, s => s.Value)
                        };
                    }
                    finally
                    {
                        _hostSecretsLock.Release();
                    }
                }

                return _hostSecrets;
            }
        }

        public async virtual Task<IDictionary<string, string>> GetFunctionSecretsAsync(string functionName, bool merged = false)
        {
            using (_metricsLogger.LatencyEvent(GetMetricEventName(MetricEventNames.SecretManagerGetFunctionSecrets), functionName))
            {
                if (string.IsNullOrEmpty(functionName))
                {
                    throw new ArgumentNullException(nameof(functionName));
                }

                functionName = functionName.ToLowerInvariant();
                Dictionary<string, string> functionSecrets;
                _secretsMap.TryGetValue(functionName, out functionSecrets);

                if (functionSecrets == null)
                {
                    FunctionSecrets secrets = await LoadFunctionSecretsAsync(functionName);
                    if (secrets == null)
                    {
                        // no secrets exist for this function so generate them
                        string message = string.Format(Resources.TraceFunctionSecretGeneration, functionName);
                        _logger.LogDebug(message);
                        secrets = GenerateFunctionSecrets();

                        await PersistSecretsAsync(secrets, functionName);
                    }

                    try
                    {
                        // Read all secrets, which will run the keys through the appropriate readers
                        secrets.Keys = secrets.Keys.Select(k => _keyValueConverterFactory.ReadKey(k)).ToList();
                    }
                    catch (CryptographicException ex)
                    {
                        string message = string.Format(Resources.TraceNonDecryptedFunctionSecretRefresh, functionName, ex);
                        _logger?.LogDebug(message);
                        await PersistSecretsAsync(secrets, functionName, true);
                        secrets = GenerateFunctionSecrets(secrets);
                        await RefreshSecretsAsync(secrets, functionName);
                    }

                    if (secrets.HasStaleKeys)
                    {
                        _logger.LogDebug(string.Format(Resources.TraceStaleFunctionSecretRefresh, functionName));
                        await RefreshSecretsAsync(secrets, functionName);
                    }

                    Dictionary<string, string> result = secrets.Keys.ToDictionary(s => s.Name, s => s.Value);

                    functionSecrets = _secretsMap.AddOrUpdate(functionName, result, (n, r) => result);
                }

                if (merged)
                {
                    // If merged is true, we combine function specific keys with host level function keys,
                    // prioritizing function specific keys
                    HostSecretsInfo hostSecrets = await GetHostSecretsAsync();
                    Dictionary<string, string> hostFunctionSecrets = hostSecrets.FunctionKeys;

                    functionSecrets = functionSecrets.Union(hostFunctionSecrets.Where(s => !functionSecrets.ContainsKey(s.Key)))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                }

                return functionSecrets;
            }
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
                    masterKey = GenerateSecret();
                    result = OperationResult.Created;
                }
                else
                {
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

        private async Task<KeyOperationResult> AddOrUpdateSecretAsync(ScriptSecretsType secretsType, string keyScope,
            string secretName, string secret, Func<ScriptSecrets> secretsFactory)
        {
            OperationResult result = OperationResult.NotFound;

            secret = secret ?? GenerateSecret();

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
            }, secretsFactory).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception.InnerException is KeyVaultErrorException)
                {
                    result = OperationResult.Forbidden;
                }
            });

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

        private Task<FunctionSecrets> LoadFunctionSecretsAsync(string functionName)
            => LoadSecretsAsync<FunctionSecrets>(functionName);

        private async Task<T> LoadSecretsAsync<T>(string keyScope = null) where T : ScriptSecrets
        {
            ScriptSecretsType type = GetSecretsType<T>();

            var result = await LoadSecretsAsync(type, keyScope);

            return result as T;
        }

        private async Task<ScriptSecrets> LoadSecretsAsync(ScriptSecretsType type, string keyScope)
        {
            return await _repository.ReadAsync(type, keyScope).ConfigureAwait(false);
        }

        private static ScriptSecretsType GetSecretsType<T>() where T : ScriptSecrets
        {
            return typeof(HostSecrets).IsAssignableFrom(typeof(T))
                ? ScriptSecretsType.Host
                : ScriptSecretsType.Function;
        }

        private HostSecrets GenerateHostSecrets()
        {
            return new HostSecrets
            {
                MasterKey = GenerateKey(ScriptConstants.DefaultMasterKeyName),
                FunctionKeys = new List<Key>
            {
                GenerateKey(ScriptConstants.DefaultFunctionKeyName)
            },
                SystemKeys = new List<Key>()
            };
        }

        private HostSecrets GenerateHostSecrets(HostSecrets secrets)
        {
            if (secrets.MasterKey.IsEncrypted)
            {
                secrets.MasterKey.Value = GenerateSecret();
            }
            secrets.SystemKeys = RegenerateKeys(secrets.SystemKeys);
            secrets.FunctionKeys = RegenerateKeys(secrets.FunctionKeys);
            return secrets;
        }

        private FunctionSecrets GenerateFunctionSecrets()
        {
            return new FunctionSecrets
            {
                Keys = new List<Key>
                {
                    GenerateKey(ScriptConstants.DefaultFunctionKeyName)
                }
            };
        }

        private FunctionSecrets GenerateFunctionSecrets(FunctionSecrets secrets)
        {
            secrets.Keys = RegenerateKeys(secrets.Keys);
            return secrets;
        }

        private IList<Key> RegenerateKeys(IList<Key> list)
        {
            return list.Select(k =>
            {
                if (k.IsEncrypted)
                {
                    k.Value = GenerateSecret();
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
                string decryptionKey = SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey);
                if (!string.IsNullOrEmpty(decryptionKey))
                {
                    SHA256Managed hash = new SHA256Managed();
                    byte[] hashBytes = hash.ComputeHash(Encoding.UTF8.GetBytes(decryptionKey));
                    secrets.DecryptionKeyId = Convert.ToBase64String(hashBytes);
                }

                string[] secretBackups = await _repository.GetSecretSnapshots(secrets.SecretsType, keyScope);

                if (secretBackups.Length >= ScriptConstants.MaximumSecretBackupCount)
                {
                    string message = string.Format(Resources.ErrorTooManySecretBackups, ScriptConstants.MaximumSecretBackupCount, string.IsNullOrEmpty(keyScope) ? "host" : keyScope, await AnalyzeSnapshots(secretBackups));
                    _logger?.LogDebug(message);
                    throw new InvalidOperationException(message);
                }
                await _repository.WriteSnapshotAsync(secretsType, keyScope, secrets);
            }
            else
            {
                await _repository.WriteAsync(secretsType, keyScope, secrets);
            }
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

        private Key GenerateKey(string name = null)
        {
            string secret = GenerateSecret();

            return CreateKey(name, secret);
        }

        private Key CreateKey(string name, string secret)
        {
            var key = new Key(name, secret);

            return _keyValueConverterFactory.WriteKey(key);
        }

        internal static string GenerateSecret()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] data = new byte[40];
                rng.GetBytes(data);
                string secret = Convert.ToBase64String(data);

                // Replace pluses as they are problematic as URL values
                return secret.Replace('+', 'a');
            }
        }

        private void OnSecretsChanged(object sender, SecretsChangedEventArgs e)
        {
            // clear the cached secrets if they exist
            // they'll be reloaded on demand next time
            if (e.SecretsType == ScriptSecretsType.Host)
            {
                _hostSecrets = null;
            }
            else
            {
                if (string.IsNullOrEmpty(e.Name))
                {
                    _secretsMap.Clear();
                }
                else
                {
                    _secretsMap.TryRemove(e.Name, out _);
                }
            }
        }

        private async Task<string> AnalyzeSnapshots(string[] secretBackups)
        {
            string analyzeResult = string.Empty;
            try
            {
                List<ScriptSecrets> shapShots = new List<ScriptSecrets>();
                foreach (string secretPath in secretBackups)
                {
                    ScriptSecrets secrets = await _repository.ReadAsync(ScriptSecretsType.Function, Path.GetFileNameWithoutExtension(secretPath));
                    shapShots.Add(secrets);
                }
                string[] hosts = shapShots.Select(x => x.HostName).Distinct().ToArray();
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
    }
}