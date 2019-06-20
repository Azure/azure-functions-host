// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest.Azure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// An <see cref="ISecretsRepository"/> implementation that uses the key vault as the backing store.
    /// </summary>
    public class KubernetesSecretsRepository : ISecretsRepository, IDisposable
    {
        // host.master = value
        private const string MasterKey = "host.master";
        // host.function.{keyName} = value
        private const string HostFunctionKeyPrefix = "host.function.";
        // host.systemKey.{keyName} = value
        private const string SystemKeyPrefix = "host.systemKey.";
        // functions.{functionName}.{keyName} = value
        private const string FunctionKeyPrefix = "functions.";
        private readonly IEnvironment _environment;
        private readonly IKubernetesClient _kubernetesClient;
        private bool _disposed;

        public KubernetesSecretsRepository(IEnvironment environment, IKubernetesClient kubernetesClient)
        {
            _environment = environment;
            _kubernetesClient = kubernetesClient;
            _kubernetesClient.OnSecretChange(() =>
            {
                SecretsChanged?.Invoke(this, new SecretsChangedEventArgs { SecretsType = ScriptSecretsType.Host });
                SecretsChanged?.Invoke(this, new SecretsChangedEventArgs { SecretsType = ScriptSecretsType.Function });
            });
        }

        public event EventHandler<SecretsChangedEventArgs> SecretsChanged;

        public bool IsEncryptionSupported => false;

        public async Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName)
        {
            return type == ScriptSecretsType.Host ? await ReadHostSecrets() : await ReadFunctionSecrets(functionName);
        }

        public async Task WriteAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        {
            if (!_kubernetesClient.IsWritable)
            {
                throw new InvalidOperationException($"{nameof(KubernetesSecretsRepository)} is readonly when no {EnvironmentSettingNames.AzureWebJobsKubernetesSecretName} is specified.");
            }

            var newKeys = await Mergekeys(type, functionName, secrets);
            await _kubernetesClient.UpdateSecrets(newKeys);
        }

        private async Task<IDictionary<string, string>> Mergekeys(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        {
            var currentKeys = await _kubernetesClient.GetSecrets();
            var newKeys = new SortedDictionary<string, string>();

            if (type == ScriptSecretsType.Host)
            {
                // A host key is changing. Add all existing function keys as is
                foreach (var key in currentKeys.Where(k => k.Key.StartsWith(FunctionKeyPrefix)))
                {
                    newKeys.Add(key.Key, key.Value);
                }

                var hostSecret = secrets as HostSecrets;

                if (hostSecret?.MasterKey != null)
                {
                    newKeys[MasterKey] = hostSecret.MasterKey.Value;
                }

                if (hostSecret?.FunctionKeys != null)
                {
                    foreach (var key in hostSecret.FunctionKeys)
                    {
                        newKeys[$"{HostFunctionKeyPrefix}{key.Name}"] = key.Value;
                    }
                }

                if (hostSecret?.SystemKeys != null)
                {
                    foreach (var key in hostSecret.SystemKeys)
                    {
                        newKeys[$"{SystemKeyPrefix}{key.Name}"] = key.Value;
                    }
                }
            }
            else
            {
                // A function key is changing. Add all host and other functions keys
                foreach (var key in currentKeys.Where(k => !k.Key.StartsWith($"{FunctionKeyPrefix}.{functionName}")))
                {
                    newKeys.Add(key.Key, key.Value);
                }

                var functionSecret = secrets as FunctionSecrets;
                if (functionSecret?.Keys != null)
                {
                    foreach (var key in functionSecret.Keys)
                    {
                        newKeys[$"{FunctionKeyPrefix}{functionName}.{key.Name}"] = key.Value;
                    }
                }
            }
            return newKeys;
        }

        /// <summary>
        /// no-op - allow stale secrets to remain
        /// </summary>
        public async Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger)
            => await Task.Yield();

        /// <summary>
        /// Runtime is not responsible for encryption so this code will never be executed.
        /// </summary>
        public Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
            => throw new NotSupportedException();

        /// <summary>
        /// Runtime is not responsible for encryption so this code will never be executed.
        /// </summary>
        public Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName)
            => throw new NotSupportedException();

        private async Task<ScriptSecrets> ReadHostSecrets()
        {
            IDictionary<string, string> secrets = await _kubernetesClient.GetSecrets();

            HostSecrets hostSecrets = new HostSecrets()
            {
                FunctionKeys = new List<Key>(),
                SystemKeys = new List<Key>()
            };

            foreach (var pair in secrets)
            {
                if (pair.Key.StartsWith(MasterKey))
                {
                    hostSecrets.MasterKey = new Key("master", pair.Value);
                }
                else if (pair.Key.StartsWith(HostFunctionKeyPrefix))
                {
                    hostSecrets.FunctionKeys.Add(ParseKeyWithPrefix(HostFunctionKeyPrefix, pair.Key, pair.Value));
                }
                else if (pair.Key.StartsWith(SystemKeyPrefix))
                {
                    hostSecrets.SystemKeys.Add(ParseKeyWithPrefix(SystemKeyPrefix, pair.Key, pair.Value));
                }
            }

            return hostSecrets?.MasterKey == null ? null : hostSecrets;
        }

        private async Task<ScriptSecrets> ReadFunctionSecrets(string functionName)
        {
            IDictionary<string, string> secrets = await _kubernetesClient.GetSecrets();
            var prefix = $"{FunctionKeyPrefix}{functionName}.";

            var functionSecrets = new FunctionSecrets()
            {
                Keys = secrets
                    .Where(p => p.Key.StartsWith(prefix))
                    .Select(p => ParseKeyWithPrefix(prefix, p.Key, p.Value))
                    .ToList()
            };

            return functionSecrets.Keys.Count == 0 ? null : functionSecrets;
        }

        private Key ParseKeyWithPrefix(string prefix, string key, string value)
            => new Key(key.Substring(prefix.Length), value);

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing && _kubernetesClient is IDisposable tmp)
                {
                    tmp.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
