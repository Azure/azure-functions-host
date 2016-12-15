// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class SecretManager : IDisposable, ISecretManager
    {
        private readonly string _secretsPath;
        private readonly ConcurrentDictionary<string, Dictionary<string, string>> _secretsMap = new ConcurrentDictionary<string, Dictionary<string, string>>();
        private readonly IKeyValueConverterFactory _keyValueConverterFactory;
        private readonly AutoRecoveringFileSystemWatcher _fileWatcher;
        private readonly string _hostSecretsPath;
        private readonly TraceWriter _traceWriter;
        private HostSecretsInfo _hostSecrets;

        // for testing
        public SecretManager()
        {
        }

        public SecretManager(ScriptSettingsManager settingsManager, string secretsPath, TraceWriter traceWriter, bool createHostSecretsIfMissing = false)
            : this(secretsPath, new DefaultKeyValueConverterFactory(settingsManager), traceWriter, createHostSecretsIfMissing)
        {
        }

        public SecretManager(string secretsPath, IKeyValueConverterFactory keyValueConverterFactory, TraceWriter traceWriter, bool createHostSecretsIfMissing = false)
        {
            _traceWriter = traceWriter.WithSource(ScriptConstants.TraceSourceSecretManagement);
            _secretsPath = secretsPath;
            _hostSecretsPath = Path.Combine(_secretsPath, ScriptConstants.HostMetadataFileName);
            _keyValueConverterFactory = keyValueConverterFactory;

            Directory.CreateDirectory(_secretsPath);

            _fileWatcher = new AutoRecoveringFileSystemWatcher(_secretsPath, "*.json");

            _fileWatcher.Changed += OnChanged;

            if (createHostSecretsIfMissing)
            {
                // The SecretManager implementation of GetHostSecrets will
                // create a host secret if one is not present.
                GetHostSecrets();
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
                _fileWatcher?.Dispose();
            }
        }

        public virtual HostSecretsInfo GetHostSecrets()
        {
            if (_hostSecrets == null)
            {
                HostSecrets hostSecrets;

                if (!TryLoadSecrets(_hostSecretsPath, out hostSecrets))
                {
                    _traceWriter.Verbose(Resources.TraceHostSecretGeneration);
                    hostSecrets = GenerateHostSecrets();
                    PersistSecrets(hostSecrets, _hostSecretsPath);
                }

                // Host secrets will be in the original persisted state at this point (e.g. encrypted),
                // so we read the secrets running them through the appropriate readers
                hostSecrets = ReadHostSecrets(hostSecrets);

                // If the persistence state of any of our secrets is stale (e.g. the encryption key has been rotated), update
                // the state and persist the secrets
                if (hostSecrets.HasStaleKeys)
                {
                    _traceWriter.Verbose(Resources.TraceStaleHostSecretRefresh);
                    RefreshSecrets(hostSecrets, _hostSecretsPath);
                }

                _hostSecrets = new HostSecretsInfo
                {
                    MasterKey = hostSecrets.MasterKey.Value,
                    FunctionKeys = hostSecrets.FunctionKeys.ToDictionary(s => s.Name, s => s.Value)
                };
            }

            return _hostSecrets;
        }

        public virtual IDictionary<string, string> GetFunctionSecrets(string functionName, bool merged = false)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                throw new ArgumentNullException(nameof(functionName));
            }

            functionName = functionName.ToLowerInvariant();

            var functionSecrets = _secretsMap.GetOrAdd(functionName, n =>
            {
                FunctionSecrets secrets;
                string secretsFilePath = GetFunctionSecretsFilePath(functionName);
                if (!TryLoadFunctionSecrets(functionName, out secrets, secretsFilePath))
                {
                    _traceWriter.VerboseFormat(Resources.TraceFunctionSecretGeneration, functionName);
                    secrets = new FunctionSecrets
                    {
                        Keys = new List<Key>
                        {
                            GenerateKey(ScriptConstants.DefaultFunctionKeyName)
                        }
                    };

                    PersistSecrets(secrets, secretsFilePath);
                }

                // Read all secrets, which will run the keys through the appropriate readers
                secrets.Keys = secrets.Keys.Select(k => _keyValueConverterFactory.ReadKey(k)).ToList();

                if (secrets.HasStaleKeys)
                {
                    _traceWriter.VerboseFormat(Resources.TraceStaleFunctionSecretRefresh, functionName);
                    RefreshSecrets(secrets, secretsFilePath);
                }

                return secrets.Keys.ToDictionary(s => s.Name, s => s.Value);
            });

            if (merged)
            {
                // If merged is true, we combine function specific keys with host level function keys,
                // prioritizing function specific keys
                Dictionary<string, string> hostFunctionSecrets = GetHostSecrets().FunctionKeys;

                functionSecrets = functionSecrets.Union(hostFunctionSecrets.Where(s => !functionSecrets.ContainsKey(s.Key)))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            return functionSecrets;
        }

        public KeyOperationResult AddOrUpdateFunctionSecret(string secretName, string secret, string functionName = null)
        {
            string secretsFilePath;
            ScriptSecretsType secretsType;
            Func<ScriptSecrets> secretsFactory = null;

            if (functionName != null)
            {
                secretsFilePath = GetFunctionSecretsFilePath(functionName);
                secretsType = ScriptSecretsType.Function;
                secretsFactory = () => new FunctionSecrets(new List<Key>());
            }
            else
            {
                secretsFilePath = _hostSecretsPath;
                secretsType = ScriptSecretsType.Host;
                secretsFactory = GenerateHostSecrets;
            }

            KeyOperationResult result = AddOrUpdateSecret(secretsType, secretsFilePath, secretName, secret, secretsFactory);

            _traceWriter.InfoFormat(Resources.TraceAddOrUpdateFunctionSecret, secretsType, secretName, functionName ?? "host", result.Result);

            return result;
        }

        public KeyOperationResult SetMasterKey(string value = null)
        {
            HostSecrets secrets;
            if (!TryLoadSecrets(_hostSecretsPath, out secrets))
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

            PersistSecrets(secrets, _hostSecretsPath);

            _traceWriter.InfoFormat(Resources.TraceMasterKeyCreatedOrUpdated, result);

            return new KeyOperationResult(masterKey, result);
        }

        public bool DeleteSecret(string secretName, string functionName = null)
        {
            string secretsFilePath = _hostSecretsPath;
            ScriptSecretsType secretsType = ScriptSecretsType.Host;

            if (functionName != null)
            {
                secretsFilePath = GetFunctionSecretsFilePath(functionName);
                secretsType = ScriptSecretsType.Function;
            }

            bool deleted = ModifyFunctionSecret(secretsType, secretsFilePath, secretName, (secrets, key) =>
            {
                secrets?.RemoveKey(key);
                return secrets;
            });

            if (deleted)
            {
                string target = secretsType == ScriptSecretsType.Function
                    ? $"Function ('{functionName}')"
                    : "Host";
                _traceWriter.InfoFormat(Resources.TraceSecretDeleted, target, secretName);
            }

            return deleted;
        }

        private KeyOperationResult AddOrUpdateSecret(ScriptSecretsType secretsType, string secretFilePath, string secretName, string secret, Func<ScriptSecrets> secretsFactory)
        {
            OperationResult result = OperationResult.NotFound;

            secret = secret ?? GenerateSecret();

            ModifyFunctionSecrets(secretsType, secretFilePath, secrets =>
            {
                Key key = secrets.GetFunctionKey(secretName);

                if (key == null)
                {
                    key = new Key(secretName, secret);
                    secrets.AddKey(key);
                    result = OperationResult.Created;
                }
                else if (secrets.RemoveKey(key))
                {
                    key = CreateKey(secretName, secret);
                    secrets.AddKey(key);

                    result = OperationResult.Updated;
                }

                return secrets;
            }, secretsFactory);

            return new KeyOperationResult(secret, result);
        }

        private static bool ModifyFunctionSecret(ScriptSecretsType secretsType, string secretFilePath, string secretName, Func<ScriptSecrets, Key, ScriptSecrets> keyChangeHandler, Func<ScriptSecrets> secretFactory = null)
        {
            bool secretFound = false;

            ModifyFunctionSecrets(secretsType, secretFilePath, secrets =>
            {
                Key key = secrets?.GetFunctionKey(secretName);

                if (key != null)
                {
                    secretFound = true;

                    secrets = keyChangeHandler(secrets, key);
                }

                return secrets;
            }, secretFactory);

            return secretFound;
        }

        private static void ModifyFunctionSecrets(ScriptSecretsType secretsType, string secretFilePath, Func<ScriptSecrets, ScriptSecrets> changeHandler, Func<ScriptSecrets> secretFactory)
        {
            ScriptSecrets currentSecrets;

            if (!TryLoadSecrets(secretsType, secretFilePath, out currentSecrets))
            {
                currentSecrets = secretFactory?.Invoke();
            }

            var newSecrets = changeHandler(currentSecrets);

            if (newSecrets != null)
            {
                PersistSecrets(newSecrets, secretFilePath);
            }
        }

        private bool TryLoadFunctionSecrets(string functionName, out FunctionSecrets secrets, string filePath = null)
        {
            secrets = null;
            string secretsFilePath = filePath ?? GetFunctionSecretsFilePath(functionName);

            return TryLoadSecrets(secretsFilePath, out secrets);
        }

        private static bool TryLoadSecrets(ScriptSecretsType secretsType, string filePath, out ScriptSecrets secrets)
            => TryLoadSecrets(filePath, s => ScriptSecretSerializer.DeserializeSecrets(secretsType, s), out secrets);

        private static bool TryLoadSecrets<T>(string filePath, out T secrets) where T : ScriptSecrets
        {
            ScriptSecrets deserializedSecrets;
            TryLoadSecrets(filePath, ScriptSecretSerializer.DeserializeSecrets<T>, out deserializedSecrets);
            secrets = deserializedSecrets as T;

            return secrets != null;
        }

        private static bool TryLoadSecrets(string filePath, Func<string, ScriptSecrets> deserializationHandler, out ScriptSecrets secrets)
        {
            secrets = null;

            if (File.Exists(filePath))
            {
                // load the secrets file
                string secretsJson = File.ReadAllText(filePath);
                secrets = deserializationHandler(secretsJson);
            }

            return secrets != null;
        }

        private string GetFunctionSecretsFilePath(string functionName)
        {
            string secretFileName = string.Format(CultureInfo.InvariantCulture, "{0}.json", functionName);
            return Path.Combine(_secretsPath, secretFileName);
        }

        private HostSecrets GenerateHostSecrets()
        {
            return new HostSecrets
            {
                MasterKey = GenerateKey(ScriptConstants.DefaultMasterKeyName),
                FunctionKeys = new List<Key>
                {
                    GenerateKey(ScriptConstants.DefaultFunctionKeyName)
                }
            };
        }

        private void RefreshSecrets<T>(T secrets, string secretsFilePath) where T : ScriptSecrets
        {
            var refreshedSecrets = secrets.Refresh(_keyValueConverterFactory);

            PersistSecrets(refreshedSecrets, secretsFilePath);
        }

        private static void PersistSecrets<T>(T secrets, string secretsFilePath) where T : ScriptSecrets
        {
            string secretsContent = ScriptSecretSerializer.SerializeSecrets<T>(secrets);
            File.WriteAllText(secretsFilePath, secretsContent);
        }

        private HostSecrets ReadHostSecrets(HostSecrets hostSecrets)
        {
            return new HostSecrets
            {
                MasterKey = _keyValueConverterFactory.ReadKey(hostSecrets.MasterKey),
                FunctionKeys = hostSecrets.FunctionKeys.Select(k => _keyValueConverterFactory.ReadKey(k)).ToList()
            };
        }

        public void PurgeOldFiles(string rootScriptPath, TraceWriter traceWriter)
        {
            try
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
                var functionLookup = Directory.EnumerateDirectories(rootScriptPath).ToLookup(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

                var secretsDirectory = new DirectoryInfo(_secretsPath);
                if (!Directory.Exists(_secretsPath))
                {
                    return;
                }

                foreach (var secretFile in secretsDirectory.GetFiles("*.json"))
                {
                    if (string.Compare(secretFile.Name, ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        // the secrets directory contains the host secrets file in addition
                        // to function secret files
                        continue;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(secretFile.Name);
                    if (!functionLookup.Contains(fileName))
                    {
                        try
                        {
                            secretFile.Delete();
                        }
                        catch
                        {
                            // Purge is best effort
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Purge is best effort
                traceWriter.Error("An error occurred while purging secret files", ex);
            }
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

        private static string GenerateSecret()
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

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            // clear the cached secrets if they exist
            // they'll be reloaded on demand next time
            if (string.Compare(Path.GetFileName(e.FullPath), ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase) == 0)
            {
                _hostSecrets = null;
            }
            else
            {
                Dictionary<string, string> secrets;
                string name = Path.GetFileNameWithoutExtension(e.FullPath).ToLowerInvariant();
                _secretsMap.TryRemove(name, out secrets);
            }
        }
    }
}