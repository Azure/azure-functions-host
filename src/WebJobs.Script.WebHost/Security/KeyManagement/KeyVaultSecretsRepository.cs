// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest.Azure;

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

        private readonly Lazy<KeyVaultClient> _keyVaultClient;
        private readonly string _vaultName;
        private readonly IEnvironment _environment;

        public KeyVaultSecretsRepository(string secretsSentinelFilePath, string vaultName, string connectionString, ILogger logger, IEnvironment environment) : base(secretsSentinelFilePath, logger, environment)
        {
            if (secretsSentinelFilePath == null)
            {
                throw new ArgumentNullException(nameof(secretsSentinelFilePath));
            }

            _vaultName = vaultName ?? throw new ArgumentNullException(nameof(vaultName));

            _keyVaultClient = new Lazy<KeyVaultClient>(() =>
            {
                AzureServiceTokenProvider azureServiceTokenProvider = string.IsNullOrEmpty(connectionString) ? new AzureServiceTokenProvider()
                    : new AzureServiceTokenProvider(connectionString);
                return new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
            });

            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
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
            List<IEnumerable<SecretItem>> secretsPages = await GetKeyVaultSecretsPagesAsync(_keyVaultClient.Value, GetVaultBaseUrl());
            List<Task> deleteTasks = new List<Task>();
            string prefix = (type == ScriptSecretsType.Host) ? HostPrefix : FunctionPrefix + Normalize(functionName);

            foreach (SecretItem item in FindSecrets(secretsPages, x => x.Identifier.Name.StartsWith(prefix)))
            {
                // Delete only keys which no longer exist in passed-in secrets
                if (!dictionary.Keys.Contains(item.Identifier.Name))
                {
                    deleteTasks.Add(_keyVaultClient.Value.DeleteSecretAsync(GetVaultBaseUrl(), item.Identifier.Name));
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
                setTasks.Add(_keyVaultClient.Value.SetSecretAsync(GetVaultBaseUrl(), key, dictionary[key]));
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
            List<IEnumerable<SecretItem>> secretsPages = await GetKeyVaultSecretsPagesAsync(_keyVaultClient.Value, GetVaultBaseUrl());
            List<Task<SecretBundle>> tasks = new List<Task<SecretBundle>>();

            // Add master key task
            List<SecretItem> masterItems = FindSecrets(secretsPages, x => x.Identifier.Name.StartsWith(MasterKey));
            if (masterItems.Count > 0)
            {
                tasks.Add(_keyVaultClient.Value.GetSecretAsync(GetVaultBaseUrl(), masterItems[0].Identifier.Name));
            }
            else
            {
                return null;
            }

            // Add functionKey tasks
            foreach (SecretItem item in FindSecrets(secretsPages, x => x.Identifier.Name.StartsWith(FunctionKeyPrefix)))
            {
                tasks.Add(_keyVaultClient.Value.GetSecretAsync(GetVaultBaseUrl(), item.Identifier.Name));
            }

            // Add systemKey tasks
            foreach (SecretItem item in FindSecrets(secretsPages, x => x.Identifier.Name.StartsWith(SystemKeyPrefix)))
            {
                tasks.Add(_keyVaultClient.Value.GetSecretAsync(GetVaultBaseUrl(), item.Identifier.Name));
            }

            await Task.WhenAll(tasks);

            HostSecrets hostSecrets = new HostSecrets()
            {
                FunctionKeys = new List<Key>(),
                SystemKeys = new List<Key>()
            };

            foreach (Task<SecretBundle> task in tasks)
            {
                SecretBundle item = task.Result;
                if (item.SecretIdentifier.Name.StartsWith(MasterKey))
                {
                    hostSecrets.MasterKey = SecretBundleToKey(item, MasterKey);
                }
                else if (item.SecretIdentifier.Name.StartsWith(FunctionKeyPrefix))
                {
                    hostSecrets.FunctionKeys.Add(SecretBundleToKey(item, FunctionKeyPrefix));
                }
                else if (item.SecretIdentifier.Name.StartsWith(SystemKeyPrefix))
                {
                    hostSecrets.SystemKeys.Add(SecretBundleToKey(item, SystemKeyPrefix));
                }
            }

            return hostSecrets;
        }

        private async Task<ScriptSecrets> ReadFunctionSecrets(string functionName)
        {
            List<IEnumerable<SecretItem>> secretsPages = await GetKeyVaultSecretsPagesAsync(_keyVaultClient.Value, GetVaultBaseUrl());
            List<Task<SecretBundle>> tasks = new List<Task<SecretBundle>>();
            string prefix = $"{FunctionPrefix}{Normalize(functionName)}--";

            foreach (SecretItem item in FindSecrets(secretsPages, x => x.Identifier.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                tasks.Add(_keyVaultClient.Value.GetSecretAsync(GetVaultBaseUrl(), item.Identifier.Name));
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

            foreach (Task<SecretBundle> task in tasks)
            {
                SecretBundle item = task.Result;
                functionSecrets.Keys.Add(SecretBundleToKey(item, prefix));
            }

            return functionSecrets;
        }

        public static async Task<List<IEnumerable<SecretItem>>> GetKeyVaultSecretsPagesAsync(KeyVaultClient keyVaultClient, string keyVaultBaseUrl)
        {
            IPage<SecretItem> secretItems = await keyVaultClient.GetSecretsAsync(keyVaultBaseUrl);
            List<IEnumerable<SecretItem>> secretsPages = new List<IEnumerable<SecretItem>>() { secretItems };

            while (!string.IsNullOrEmpty(secretItems.NextPageLink))
            {
                secretItems = await keyVaultClient.GetSecretsNextAsync(secretItems.NextPageLink);
                secretsPages.Add(secretItems);
            }

            return secretsPages;
        }

        public static List<SecretItem> FindSecrets(List<IEnumerable<SecretItem>> secretsPages, Func<SecretItem, bool> comparison = null)
        {
            // if no comparison is provided, every item is a match
            if (comparison == null)
            {
                comparison = x => true;
            }

            var secretItems = new List<SecretItem>();
            foreach (IEnumerable<SecretItem> secretsPage in secretsPages)
            {
                foreach (SecretItem secretItem in secretsPage.Where(x => comparison(x)))
                {
                    secretItems.Add(secretItem);
                }
            }

            return secretItems;
        }

        private string GetVaultBaseUrl()
        {
            return $"https://{_vaultName}{_environment.GetVaultSuffix()}";
        }

        public static Dictionary<string, string> GetDictionaryFromScriptSecrets(ScriptSecrets secrets, string functionName)
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();
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

        private static Key SecretBundleToKey(SecretBundle item, string prefix)
        {
            return new Key()
            {
                Name = Denormalize(item.SecretIdentifier.Name.Remove(0, prefix.Length)),
                Value = item.Value,
                IsStale = false,
                IsEncrypted = false
            };
        }
    }
}
