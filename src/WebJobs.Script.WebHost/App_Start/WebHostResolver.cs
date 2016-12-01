﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class WebHostResolver : IDisposable
    {
        private static object _syncLock = new object();

        private ScriptHostConfiguration _standbyScriptHostConfig;
        private WebScriptHostManager _standbyHostManager;
        private ISecretManager _standbySecretManager;
        private WebHookReceiverManager _standbyReceiverManager;

        private ScriptHostConfiguration _activeScriptHostConfig;
        private WebScriptHostManager _activeHostManager;
        private ISecretManager _activeSecretManager;
        private WebHookReceiverManager _activeReceiverManager;

        private static ScriptSettingsManager _settingsManager;

        public WebHostResolver(ScriptSettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
        }

        public ScriptHostConfiguration GetScriptHostConfiguration(WebHostSettings settings)
        {
            if (_activeScriptHostConfig != null)
            {
                return _activeScriptHostConfig;
            }

            lock (_syncLock)
            {
                EnsureInitialized(settings);

                return _activeScriptHostConfig ?? _standbyScriptHostConfig;
            }
        }

        public ISecretManager GetSecretManager(WebHostSettings settings)
        {
            if (_activeSecretManager != null)
            {
                return _activeSecretManager;
            }

            lock (_syncLock)
            {
                EnsureInitialized(settings);

                return _activeSecretManager ?? _standbySecretManager;
            }
        }

        public WebScriptHostManager GetWebScriptHostManager(WebHostSettings settings)
        {
            if (_activeHostManager != null)
            {
                return _activeHostManager;
            }

            lock (_syncLock)
            {
                EnsureInitialized(settings);

                return _activeHostManager ?? _standbyHostManager;
            }
        }

        public WebHookReceiverManager GetWebHookReceiverManager(WebHostSettings settings)
        {
            if (_activeReceiverManager != null)
            {
                return _activeReceiverManager;
            }

            lock (_syncLock)
            {
                EnsureInitialized(settings);

                return _activeReceiverManager ?? _standbyReceiverManager;
            }
        }

        private void EnsureInitialized(WebHostSettings settings)
        {
            // standby mode can only change from true to false
            // When standby mode changes, we reset all instances
            var standbyMode = WebScriptHostManager.InStandbyMode;
            if (!standbyMode)
            {
                if (_activeHostManager == null)
                {
                    if (_standbyHostManager != null)
                    {
                        // reintialize app settings if we were in standby
                        ReinitializeAppSettings();
                    }

                    _activeScriptHostConfig = CreateScriptHostConfiguration(settings);
                    _activeSecretManager = GetSecretManager(_settingsManager, settings.SecretsPath);
                    _activeReceiverManager = new WebHookReceiverManager(_activeSecretManager);
                    _activeHostManager = new WebScriptHostManager(_activeScriptHostConfig, _activeSecretManager, _settingsManager, settings);

                    (_standbySecretManager as IDisposable)?.Dispose();
                    _standbyHostManager?.Dispose();
                    _standbyReceiverManager?.Dispose();

                    _standbyScriptHostConfig = null;
                    _standbySecretManager = null;
                    _standbyHostManager = null;
                    _standbyReceiverManager = null;
                    _settingsManager.Reset();
                }
            }
            else
            {
                if (_standbyHostManager == null)
                {
                    _standbyScriptHostConfig = CreateScriptHostConfiguration(settings);
                    _standbySecretManager = GetSecretManager(_settingsManager, settings.SecretsPath);
                    _standbyReceiverManager = new WebHookReceiverManager(_standbySecretManager);
                    _standbyHostManager = new WebScriptHostManager(_standbyScriptHostConfig, _standbySecretManager, _settingsManager, settings);
                }
            }
        }

        private static void ReinitializeAppSettings()
        {
            if (_settingsManager.IsAzureEnvironment)
            {
                // the nature of this is only add or update (not remove).
                // so there may be settings from standby site leak over.
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.FullName.StartsWith("EnvSettings, "));
                var envSettingType = assembly.GetType("EnvSettings.SettingsProcessor", throwOnError: true);
                var startMethod = envSettingType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
                startMethod.Invoke(null, new object[0]);
            }
        }

        private static ScriptHostConfiguration CreateScriptHostConfiguration(WebHostSettings settings)
        {
            InitializeFileSystem(settings.ScriptPath);

            var scriptHostConfig = new ScriptHostConfiguration()
            {
                RootScriptPath = settings.ScriptPath,
                RootLogPath = settings.LogPath,
                FileLoggingMode = FileLoggingMode.DebugOnly,
                TraceWriter = settings.TraceWriter
            };

            // If running on Azure Web App, derive the host ID from the default subdomain
            // Otherwise, derive it from machine name and folder name
            string hostId = _settingsManager.AzureWebsiteDefaultSubdomain
                ?? MakeValidHostId($"{Environment.MachineName}-{Path.GetFileName(Environment.CurrentDirectory)}");

            if (!String.IsNullOrEmpty(hostId))
            {
                scriptHostConfig.HostConfig.HostId = hostId;
            }

            return scriptHostConfig;
        }

        //lowercase letters, numbers and dashes.
        //cannot start or end with dash.
        //cannot have consecutive dashes.
        //max length 32.
        private static string MakeValidHostId(string id)
        {
            var sb = new StringBuilder();

            //filter for valid characters
            foreach (var c in id)
            {
                if (c == '-')
                {
                    //dashes are valid
                    //but it cannot start with one
                    //nor can it have consecutive dashes
                    if (sb.Length != 0 && sb[sb.Length - 1] != '-')
                    {
                        sb.Append(c);
                    }
                }
                else if (char.IsDigit(c))
                {
                    //digits are valid
                    sb.Append(c);
                }
                else if (char.IsLetter(c))
                {
                    //letters are valid but must be lowercase
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            //it cannot end with a dash
            if (sb.Length > 0 && sb[sb.Length - 1] == '-')
            {
                sb.Length -= 1;
            }

            //length cannot exceed 32
            const int MaximumHostIdLength = 32;
            if (sb.Length > MaximumHostIdLength)
            {
                sb.Length = MaximumHostIdLength;
            }

            return sb.ToString();
        }

        private static void InitializeFileSystem(string scriptPath)
        {
            if (ScriptSettingsManager.Instance.IsAzureEnvironment)
            {
                // When running on Azure, we kick this off on the background
                Task.Run(() =>
                {
                    string home = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
                    if (!string.IsNullOrEmpty(home))
                    {
                        // Delete hostingstart.html if any. Azure creates that in all sites by default
                        string hostingStart = Path.Combine(scriptPath, "hostingstart.html");
                        if (File.Exists(hostingStart))
                        {
                            File.Delete(hostingStart);
                        }

                        // Create the tools folder if it doesn't exist
                        string toolsPath = Path.Combine(home, @"site\tools");
                        Directory.CreateDirectory(toolsPath);

                        var folders = new List<string>();
                        folders.Add(Path.Combine(home, @"site\tools"));

                        string path = Environment.GetEnvironmentVariable("PATH");
                        string additionalPaths = String.Join(";", folders);

                        // Make sure we haven't already added them. This can happen if the appdomain restart (since it's still same process)
                        if (!path.Contains(additionalPaths))
                        {
                            path = additionalPaths + ";" + path;

                            Environment.SetEnvironmentVariable("PATH", path);
                        }
                    }
                });
            }
            else
            {
                // Ensure we have our scripts directory in non-Azure scenarios
                Directory.CreateDirectory(scriptPath);
            }
        }

        private static ISecretManager GetSecretManager(ScriptSettingsManager settingsManager, string secretsPath) => new SecretManager(settingsManager, secretsPath);

        public void Dispose()
        {
            (_standbySecretManager as IDisposable)?.Dispose();
            _standbyHostManager?.Dispose();
            _standbyReceiverManager?.Dispose();

            (_activeSecretManager as IDisposable)?.Dispose();
            _activeHostManager?.Dispose();
            _activeReceiverManager?.Dispose();
        }
    }
}