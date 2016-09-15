// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class WebHostResolver : IDisposable
    {
        private static object _syncLock = new object();

        private ScriptHostConfiguration _standbyScriptHostConfig;
        private WebScriptHostManager _standbyHostManager;
        private SecretManager _standbySecretManager;
        private WebHookReceiverManager _standbyReceiverManager;

        private ScriptHostConfiguration _activeScriptHostConfig;
        private WebScriptHostManager _activeHostManager;
        private SecretManager _activeSecretManager;
        private WebHookReceiverManager _activeReceiverManager;

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

        public SecretManager GetSecretManager(WebHostSettings settings)
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

                    _activeScriptHostConfig = GetScriptHostConfiguration(settings.ScriptPath, settings.LogPath);
                    _activeSecretManager = GetSecretManager(settings.SecretsPath);
                    _activeReceiverManager = new WebHookReceiverManager(_activeSecretManager);
                    _activeHostManager = new WebScriptHostManager(_activeScriptHostConfig, _activeSecretManager, settings);

                    _standbySecretManager?.Dispose();
                    _standbyHostManager?.Dispose();
                    _standbyReceiverManager?.Dispose();

                    _standbyScriptHostConfig = null;
                    _standbySecretManager = null;
                    _standbyHostManager = null;
                    _standbyReceiverManager = null;
                }
            }
            else
            {
                if (_standbyHostManager == null)
                {
                    _standbyScriptHostConfig = GetScriptHostConfiguration(settings.ScriptPath, settings.LogPath);
                    _standbySecretManager = GetSecretManager(settings.SecretsPath);
                    _standbyReceiverManager = new WebHookReceiverManager(_standbySecretManager);
                    _standbyHostManager = new WebScriptHostManager(_standbyScriptHostConfig, _standbySecretManager, settings);
                }
            }
        }

        private static void ReinitializeAppSettings()
        {
            if (WebScriptHostManager.IsAzureEnvironment)
            {
                // the nature of this is only add or update (not remove).
                // so there may be settings from standby site leak over.
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.FullName.StartsWith("EnvSettings, "));
                var envSettingType = assembly.GetType("EnvSettings.SettingsProcessor", throwOnError: true);
                var startMethod = envSettingType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
                startMethod.Invoke(null, new object[0]);
            }
        }

        private static ScriptHostConfiguration GetScriptHostConfiguration(string scriptPath, string logPath)
        {
            string home = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
            if (!string.IsNullOrEmpty(home))
            {
                // Create the tools folder if it doesn't exist
                string toolsPath = Path.Combine(home, @"site\tools");
                Directory.CreateDirectory(toolsPath);
            }

            Directory.CreateDirectory(scriptPath);

            // Delete hostingstart.html if any. Azure creates that in all sites by default
            string hostingStart = Path.Combine(scriptPath, "hostingstart.html");
            if (File.Exists(hostingStart))
            {
                File.Delete(hostingStart);
            }

            var scriptHostConfig = new ScriptHostConfiguration()
            {
                RootScriptPath = scriptPath,
                RootLogPath = logPath,
                FileLoggingMode = FileLoggingMode.DebugOnly
            };

            // If running on Azure Web App, derive the host ID from the site name
            string hostId = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);
            if (!String.IsNullOrEmpty(hostId))
            {
                // Truncate to the max host name length if needed
                const int MaximumHostIdLength = 32;
                if (hostId.Length > MaximumHostIdLength)
                {
                    hostId = hostId.Substring(0, MaximumHostIdLength);
                }

                // Trim any trailing - as they can cause problems with queue names
                hostId = hostId.TrimEnd('-');

                scriptHostConfig.HostConfig.HostId = hostId.ToLowerInvariant();
            }

            return scriptHostConfig;
        }

        private static SecretManager GetSecretManager(string secretsPath)
        {
            var secretManager = new SecretManager(secretsPath);
            secretManager.GetHostSecrets();

            return secretManager;
        }

        public void Dispose()
        {
            _standbySecretManager?.Dispose();
            _standbyHostManager?.Dispose();
            _standbyReceiverManager?.Dispose();

            _activeSecretManager?.Dispose();
            _activeHostManager?.Dispose();
            _activeReceiverManager?.Dispose();
        }
    }
}