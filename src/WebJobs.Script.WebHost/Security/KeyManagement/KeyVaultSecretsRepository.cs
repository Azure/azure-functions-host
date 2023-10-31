// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// An <see cref="ISecretsRepository"/> implementation that uses the key vault as the backing store.
    /// </summary>
    public class KeyVaultSecretsRepository : BaseSecretsRepository
    {
        private const string HostPrefix = "host--";
        private const string FunctionPrefix = "function--";
        private const string NormalizedCharMark = "-";
        private const string MasterKey = "host--masterKey--";
        private const string FunctionKeyPrefix = "host--functionKey--";
        private const string SystemKeyPrefix = "host--systemKey--";

        private readonly Lazy<SecretClient> _secretClient;
        private readonly IEnvironment _environment;

        public KeyVaultSecretsRepository(string secretsSentinelFilePath, string vaultUri, string clientId, string clientSecret, string tenantId, ILogger logger, IEnvironment environment) : base(secretsSentinelFilePath, logger, environment)
        {
            if (string.IsNullOrEmpty(secretsSentinelFilePath))
            {
                throw new ArgumentException(nameof(secretsSentinelFilePath));
            }

            Uri keyVaultUri = string.IsNullOrEmpty(vaultUri) ? throw new ArgumentException(nameof(vaultUri)) : new Uri(vaultUri);

            _secretClient = new Lazy<SecretClient>(() =>
            {
                // If clientSecret and tenantId are provided, use ClientSecret credential; otherwise use managed identity
                TokenCredential credential = !string.IsNullOrEmpty(clientSecret) && !string.IsNullOrEmpty(tenantId)
                    ? new ClientSecretCredential(tenantId, clientId, clientSecret)
                    : new ChainedTokenCredential(new ManagedIdentityCredential(clientId), new ManagedIdentityCredential());

                return new SecretClient(keyVaultUri, credential);
            });

            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        // For testing
        internal KeyVaultSecretsRepository(SecretClient secretClient, string secretsSentinelFilePath, ILogger logger, IEnvironment environment) : base(secretsSentinelFilePath, logger, environment)
        {
            _secretClient = new Lazy<SecretClient>(() => secretClient);
        }

        public override bool IsEncryptionSupported
        {
            get
            {
                return true;
            }
        }

        public override string Name => nameof(KeyVaultSecretsRepository);

        public override async Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName)
        {
            return type == ScriptSecretsType.Host ? await ReadHostSecrets() : await ReadFunctionSecrets(functionName);
        }

        public override async Task WriteAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        {
            // Get secrets as dictionary
            Dictionary<string, string> dictionary = GetDictionaryFromScriptSecrets(secrets, functionName);

            // Delete existing keys
            AsyncPageable<SecretProperties> secretsPages = GetKeyVaultSecretsPagesAsync(_secretClient.Value);
            List<Task> deleteTasks = new List<Task>();
            string prefix = (type == ScriptSecretsType.Host) ? HostPrefix : FunctionPrefix + Normalize(functionName);

            foreach (SecretProperties item in await FindSecrets(secretsPages, x => x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                // Delete only keys which no longer exist in passed-in secrets
                if (!dictionary.Keys.Contains(item.Name))
                {
                    Logger?.KeyVaultSecretRepoDeleteKey(item.Name);
                    deleteTasks.Add(_secretClient.Value.StartDeleteSecretAsync(item.Name));
                }
            }

            if (deleteTasks.Any())
            {
                await Task.WhenAll(deleteTasks);
            }

            // Set new secrets
            List<Task> setTasks = new List<Task>();
            foreach (string key in dictionary.Keys)
            {
                Logger?.KeyVaultSecretRepoSetKey(key);
                setTasks.Add(_secretClient.Value.SetSecretAsync(key, dictionary[key]));
            }
            await Task.WhenAll(setTasks);

            string filePath = GetSecretsSentinelFilePath(type, functionName);
            await FileUtility.WriteAsync(filePath, DateTime.UtcNow.ToString());
        }

        public override async Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger)
        {
            // no-op - allow stale secrets to remain
            await Task.Yield();
        }

        public override Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        {
            //Runtime is not responsible for encryption so this code will never be executed.
            throw new NotSupportedException();
        }

        public override Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName)
        {
            //Runtime is not responsible for encryption so this code will never be executed.
            throw new NotSupportedException();
        }

        private async Task<ScriptSecrets> ReadHostSecrets()
        {
            AsyncPageable<SecretProperties> secretsPages = GetKeyVaultSecretsPagesAsync(_secretClient.Value);
            List<Task<Response<KeyVaultSecret>>> tasks = new List<Task<Response<KeyVaultSecret>>>();

            // Add master key task
            List<SecretProperties> masterItems = await FindSecrets(secretsPages, x => x.Name.StartsWith(MasterKey, StringComparison.OrdinalIgnoreCase));
            if (masterItems.Count > 0)
            {
                tasks.Add(_secretClient.Value.GetSecretAsync(masterItems[0].Name));
            }
            else
            {
                return null;
            }

            // Add functionKey tasks
            foreach (SecretProperties item in await FindSecrets(secretsPages, x => x.Name.StartsWith(FunctionKeyPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                tasks.Add(_secretClient.Value.GetSecretAsync(item.Name));
            }

            // Add systemKey tasks
            foreach (SecretProperties item in await FindSecrets(secretsPages, x => x.Name.StartsWith(SystemKeyPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                tasks.Add(_secretClient.Value.GetSecretAsync(item.Name));
            }

            await Task.WhenAll(tasks);

            HostSecrets hostSecrets = new HostSecrets()
            {
                FunctionKeys = new List<Key>(),
                SystemKeys = new List<Key>()
            };

            foreach (Task<Response<KeyVaultSecret>> task in tasks)
            {
                KeyVaultSecret item = task.Result;
                if (item.Name.StartsWith(MasterKey, StringComparison.OrdinalIgnoreCase))
                {
                    hostSecrets.MasterKey = KeyVaultSecretToKey(item, MasterKey);
                }
                else if (item.Name.StartsWith(FunctionKeyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    hostSecrets.FunctionKeys.Add(KeyVaultSecretToKey(item, FunctionKeyPrefix));
                }
                else if (item.Name.StartsWith(SystemKeyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    hostSecrets.SystemKeys.Add(KeyVaultSecretToKey(item, SystemKeyPrefix));
                }
            }

            return hostSecrets;
        }

        private async Task<ScriptSecrets> ReadFunctionSecrets(string functionName)
        {
            AsyncPageable<SecretProperties> secretsPages = GetKeyVaultSecretsPagesAsync(_secretClient.Value);
            List<Task<Response<KeyVaultSecret>>> tasks = new List<Task<Response<KeyVaultSecret>>>();
            string prefix = $"{FunctionPrefix}{Normalize(functionName)}--";

            foreach (SecretProperties item in await FindSecrets(secretsPages, x => x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                tasks.Add(_secretClient.Value.GetSecretAsync(item.Name));
            }

            if (!tasks.Any())
            {
                return null;
            }
            await Task.WhenAll(tasks);

            FunctionSecrets functionSecrets = new FunctionSecrets()
            {
                Keys = new List<Key>()
            };

            foreach (Task<Response<KeyVaultSecret>> task in tasks)
            {
                KeyVaultSecret item = task.Result;
                functionSecrets.Keys.Add(KeyVaultSecretToKey(item, prefix));
            }

            return functionSecrets;
        }

        public static AsyncPageable<SecretProperties> GetKeyVaultSecretsPagesAsync(SecretClient secretClient)
        {
            return secretClient.GetPropertiesOfSecretsAsync();
        }

        public static async Task<List<SecretProperties>> FindSecrets(AsyncPageable<SecretProperties> secretsPages, Func<SecretProperties, bool> comparison = null)
        {
            // if no comparison is provided, every item is a match
            if (comparison == null)
            {
                comparison = x => true;
            }

            var secretsList = new List<SecretProperties>();

            await foreach (Page<SecretProperties> page in secretsPages.AsPages())
            {
                foreach (SecretProperties secret in page.Values.Where(x => comparison(x)))
                {
                    secretsList.Add(secret);
                }
            }

            return secretsList;
        }

        public static Dictionary<string, string> GetDictionaryFromScriptSecrets(ScriptSecrets secrets, string functionName)
        {
            Dictionary<string, string> dic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HostSecrets hostSecrets = secrets as HostSecrets;
            FunctionSecrets functionSecrets = secrets as FunctionSecrets;
            if (hostSecrets != null)
            {
                if (hostSecrets.MasterKey != null)
                {
                    dic.Add(MasterKey + "master", hostSecrets.MasterKey.Value);
                }

                if (hostSecrets.FunctionKeys != null)
                {
                    foreach (Key key in hostSecrets.FunctionKeys)
                    {
                        dic.Add($"{FunctionKeyPrefix}{Normalize(key.Name)}", key.Value);
                    }
                }

                if (hostSecrets.SystemKeys != null)
                {
                    foreach (Key key in hostSecrets.SystemKeys)
                    {
                        dic.Add($"{SystemKeyPrefix}{Normalize(key.Name)}", key.Value);
                    }
                }
            }
            else if (functionSecrets != null)
            {
                if (functionSecrets.Keys != null)
                {
                    foreach (Key key in functionSecrets.Keys)
                    {
                        dic.Add($"{FunctionPrefix}{Normalize(functionName)}--{Normalize(key.Name)}", key.Value);
                    }
                }
            }
            return dic;
        }

        public static string Normalize(string name)
        {
            string result = string.Empty;
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    result += c;
                }
                else
                {
                    string charValue = ((int)c).ToString("D3");
                    result += NormalizedCharMark + charValue;
                }
            }
            return result;
        }

        public static string Denormalize(string normalizedName)
        {
            string result = string.Empty;
            for (int index = 0; index < normalizedName.Length; index++)
            {
                if ((index + NormalizedCharMark.Length + 3 <= normalizedName.Length) &&
                    (normalizedName.Substring(index, NormalizedCharMark.Length) == NormalizedCharMark))
                {
                    string numberString = normalizedName.Substring(index + NormalizedCharMark.Length, 3);
                    int parseResult;
                    if (int.TryParse(numberString, out parseResult))
                    {
                        result += (char)parseResult;
                        index += NormalizedCharMark.Length + 2;
                    }
                    else
                    {
                        result += normalizedName[index];
                    }
                }
                else
                {
                    result += normalizedName[index];
                }
            }
            return result;
        }

        private static Key KeyVaultSecretToKey(KeyVaultSecret item, string prefix)
        {
            return new Key()
            {
                Name = Denormalize(item.Properties.Name.Remove(0, prefix.Length)),
                Value = item.Value,
                IsStale = false,
                IsEncrypted = false
            };
        }
    }
}
