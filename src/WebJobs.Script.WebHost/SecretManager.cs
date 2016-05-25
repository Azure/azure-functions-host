// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class SecretManager
    {
        private readonly string _secretsPath;
        private readonly ConcurrentDictionary<string, FunctionSecrets> _secretsMap = new ConcurrentDictionary<string, FunctionSecrets>();
        private readonly FileSystemWatcher _fileWatcher;
        private HostSecrets _hostSecrets;

        // for testing
        public SecretManager()
        {
        }

        public SecretManager(string secretsPath)
        {
            _secretsPath = secretsPath;

            Directory.CreateDirectory(_secretsPath);

            _fileWatcher = new FileSystemWatcher(_secretsPath, "*.json")
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnChanged;
            _fileWatcher.Created += OnChanged;
            _fileWatcher.Deleted += OnChanged;
            _fileWatcher.Renamed += OnChanged;
        }

        public virtual HostSecrets GetHostSecrets()
        {
            if (_hostSecrets == null)
            {
                string secretFilePath = Path.Combine(_secretsPath, ScriptConstants.HostMetadataFileName);
                if (File.Exists(secretFilePath))
                {
                    // load the secrets file
                    string secretsJson = File.ReadAllText(secretFilePath);
                    _hostSecrets = JsonConvert.DeserializeObject<HostSecrets>(secretsJson);
                }
                else
                {
                    // initialize with new secrets and save it
                    _hostSecrets = new HostSecrets
                    {
                        MasterKey = GenerateSecretString(),
                        FunctionKey = GenerateSecretString()
                    };

                    File.WriteAllText(secretFilePath, JsonConvert.SerializeObject(_hostSecrets, Formatting.Indented));
                }
            }
            return _hostSecrets;
        }

        public virtual FunctionSecrets GetFunctionSecrets(string functionName)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                throw new ArgumentNullException("functionName");
            }

            functionName = functionName.ToLowerInvariant();

            return _secretsMap.GetOrAdd(functionName, (n) =>
            {
                FunctionSecrets secrets;
                string secretFileName = string.Format(CultureInfo.InvariantCulture, "{0}.json", functionName);
                string secretFilePath = Path.Combine(_secretsPath, secretFileName);
                if (File.Exists(secretFilePath))
                {
                    // load the secrets file
                    string secretsJson = File.ReadAllText(secretFilePath);
                    secrets = JsonConvert.DeserializeObject<FunctionSecrets>(secretsJson);
                }
                else
                {
                    // initialize with new secrets and save it
                    secrets = new FunctionSecrets
                    {
                        Key = GenerateSecretString()
                    };

                    File.WriteAllText(secretFilePath, JsonConvert.SerializeObject(secrets, Formatting.Indented));
                }

                return secrets;
            });
        }

        /// <summary>
        /// Iterate through all function secret files and remove any that don't correspond
        /// to a function.
        /// </summary>
        /// <param name="rootScriptPath">The root function directory.</param>
        /// <param name="traceWriter">The TraceWriter to log to.</param>
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

        private static string GenerateSecretString()
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
                FunctionSecrets secrets;
                string name = Path.GetFileNameWithoutExtension(e.FullPath).ToLowerInvariant();
                _secretsMap.TryRemove(name, out secrets);
            }
        }
    }
}