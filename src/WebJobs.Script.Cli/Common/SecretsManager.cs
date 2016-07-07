// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Common
{
    internal class SecretsManager : ISecretsManager
    {
        public const string AppSettingsFileName = "appsettings.json";

        public IDictionary<string, string> GetSecrets()
        {
            var appSettingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            return appSettingsFile.GetValues();
        }

        public void SetSecret(string name, string value)
        {
            var appSettingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            appSettingsFile.SetSecret(name, value);
            appSettingsFile.Commit();
        }

        public void DecryptSettings()
        {
            var settingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            if (settingsFile.IsEncrypted)
            {
                var values = settingsFile.GetValues();
                settingsFile.IsEncrypted = false;
                foreach (var pair in values)
                {
                    settingsFile.SetSecret(pair.Key, pair.Value);
                }
                settingsFile.Commit();
            }
        }

        public void EncryptSettings()
        {
            var settingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            if (!settingsFile.IsEncrypted)
            {
                var values = settingsFile.GetValues();
                settingsFile.IsEncrypted = true;
                foreach (var pair in values)
                {
                    settingsFile.SetSecret(pair.Key, pair.Value);
                }
                settingsFile.Commit();
            }
        }

        public void DeleteSecret(string name)
        {
            var settingsFile = new AppSettingsFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
            settingsFile.RemoveSetting(name);
        }

        private class AppSettingsFile
        {
            [JsonIgnore]
            private readonly string _filePath;

            public AppSettingsFile() { }

            public AppSettingsFile(string filePath)
            {
                _filePath = filePath;
                try
                {
                    var content = FileSystemHelpers.ReadAllTextFromFile(Path.Combine(Environment.CurrentDirectory, AppSettingsFileName));
                    var appSettings = JsonConvert.DeserializeObject<AppSettingsFile>(content);
                    IsEncrypted = appSettings.IsEncrypted;
                    Values = appSettings.Values;
                }
                catch
                {
                    Values = new Dictionary<string, string>();
                    IsEncrypted = true;
                }
            }

            public bool IsEncrypted { get; set; }
            public Dictionary<string, string> Values { get; set; }

            public void SetSecret(string name, string value)
            {
                if (IsEncrypted)
                {
                    Values[name] = Convert.ToBase64String(ProtectedData.Protect(Encoding.Default.GetBytes(value), null, DataProtectionScope.CurrentUser));
                }
                else
                {
                    Values[name] = value;
                };
            }

            public void RemoveSetting(string name)
            {
                if (Values.ContainsKey(name))
                {
                    Values.Remove(name);
                }
            }

            public void Commit()
            {
                FileSystemHelpers.WriteAllTextToFile(_filePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }

            public IDictionary<string, string> GetValues()
            {
                if (IsEncrypted)
                {
                    return Values.ToDictionary(k => k.Key, v => Encoding.Default.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Value), null, DataProtectionScope.CurrentUser)));
                }
                else
                {
                    return Values;
                }
            }
        }
    }
}
