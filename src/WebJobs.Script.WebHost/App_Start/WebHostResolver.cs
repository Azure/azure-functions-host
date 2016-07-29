// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebHostResolver
    {
        private static object _syncLock = new object();

        private static ScriptHostConfiguration _standbyScriptHostConfig;
        private static WebScriptHostManager _standbyHostManager;
        private static SecretManager _standbySecretManager;
        private static WebHookReceiverManager _standbyHookManager;

        private static ScriptHostConfiguration _activeScriptHostConfig;
        private static WebScriptHostManager _activeHostManager;
        private static SecretManager _activeSecretManager;
        private static WebHookReceiverManager _activeHookManager;

        public static ScriptHostConfiguration GetScriptHostConfiguration(WebHostSettings settings)
        {
            if (_activeScriptHostConfig != null)
            {
                return _activeScriptHostConfig;
            }

            lock (_syncLock)
            {
                EnsureInitialize(settings);

                return _activeScriptHostConfig ?? _standbyScriptHostConfig;
            }
        }

        public static SecretManager GetSecretManager(WebHostSettings settings)
        {
            if (_activeSecretManager != null)
            {
                return _activeSecretManager;
            }

            lock (_syncLock)
            {
                EnsureInitialize(settings);

                return _activeSecretManager ?? _standbySecretManager;
            }
        }

        public static WebScriptHostManager GetWebScriptHostManager(WebHostSettings settings)
        {
            if (_activeHostManager != null)
            {
                return _activeHostManager;
            }

            lock (_syncLock)
            {
                EnsureInitialize(settings);

                return _activeHostManager ?? _standbyHostManager;
            }
        }

        public static WebHookReceiverManager GetWebHookReceiverManager(WebHostSettings settings)
        {
            if (_activeHookManager != null)
            {
                return _activeHookManager;
            }

            lock (_syncLock)
            {
                EnsureInitialize(settings);

                return _activeHookManager ?? _standbyHookManager;
            }
        }

        public static void Reset()
        {
            _standbySecretManager?.Dispose();
            _standbyHostManager?.Dispose();
            _standbyHookManager?.Dispose();

            _standbyScriptHostConfig = null;
            _standbySecretManager = null;
            _standbyHostManager = null;
            _standbyHookManager = null;

            _activeSecretManager?.Dispose();
            _activeHostManager?.Dispose();
            _activeHookManager?.Dispose();

            _activeScriptHostConfig = null;
            _activeSecretManager = null;
            _activeHostManager = null;
            _activeHookManager = null;
        }

        private static void EnsureInitialize(WebHostSettings settings)
        {
            // standbyMode can only change from true to false
            // When standbyMode changed, we reset all instances
            var standbyMode = ScriptHost.InStandbyMode;
            if (!standbyMode)
            {
                if (_activeHostManager == null)
                {
                    if (_standbyHostManager != null)
                    {
                        // reintialize app settings if earlier in standby
                        ReinitializeAppSettings();
                    }

                    _activeScriptHostConfig = GetScriptHostConfiguration(settings.ScriptPath, settings.LogPath);
                    _activeSecretManager = GetSecretManager(settings.SecretsPath);
                    _activeHookManager = new WebHookReceiverManager(_activeSecretManager);
                    _activeHostManager = new WebScriptHostManager(_activeScriptHostConfig, _activeSecretManager, settings);

                    _standbySecretManager?.Dispose();
                    _standbyHostManager?.Dispose();
                    _standbyHookManager?.Dispose();

                    _standbyScriptHostConfig = null;
                    _standbySecretManager = null;
                    _standbyHostManager = null;
                    _standbyHookManager = null;
                }
            }
            else
            {
                if (_standbyHostManager == null)
                {
                    _standbyScriptHostConfig = GetScriptHostConfiguration(settings.ScriptPath, settings.LogPath);
                    _standbySecretManager = GetSecretManager(settings.SecretsPath);
                    _standbyHookManager = new WebHookReceiverManager(_standbySecretManager);
                    _standbyHostManager = new WebScriptHostManager(_standbyScriptHostConfig, _standbySecretManager, settings);
                }
            }
        }

        private static void ReinitializeAppSettings()
        {
            // only in azure environment
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
            string home = Environment.GetEnvironmentVariable("HOME");
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
                FileLoggingEnabled = true,
                FileWatchingEnabled = !ScriptHost.InStandbyMode
            };

            // If running on Azure Web App, derive the host ID from the site name
            string hostId = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private static SecretManager GetSecretManager(string secretsPath)
        {
            var secretManager = new SecretManager(secretsPath);
            secretManager.GetHostSecrets();
            return secretManager;
        }
    }
}