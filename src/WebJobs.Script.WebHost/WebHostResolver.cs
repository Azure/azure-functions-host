// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class WebHostResolver : IDisposable
    {
        private static object _syncLock = new object();

        private readonly ISecretManagerFactory _secretManagerFactory;
        private readonly IScriptEventManager _eventManager;
        private readonly IWebJobsRouter _router;
        private readonly ILoggerFactoryBuilder _loggerFactoryBuilder;
        private readonly WebHostSettings _settings;
        private readonly IConfiguration _configuration;
        private ScriptHostConfiguration _standbyScriptHostConfig;
        private WebScriptHostManager _standbyHostManager;

        private ScriptHostConfiguration _activeScriptHostConfig;
        private WebScriptHostManager _activeHostManager;

        private static ScriptSettingsManager _settingsManager;

        public WebHostResolver(ScriptSettingsManager settingsManager, ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager, WebHostSettings settings, IWebJobsRouter router, ILoggerFactoryBuilder loggerFactoryBuilder)
        {
            _settingsManager = settingsManager;
            _secretManagerFactory = secretManagerFactory;
            _eventManager = eventManager;
            _router = router;
            _loggerFactoryBuilder = loggerFactoryBuilder;
            _settings = settings;
        }

        public ScriptHostConfiguration GetScriptHostConfiguration() =>
            GetScriptHostConfiguration(_settings);

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

        public ISecretManager GetSecretManager() =>
            GetSecretManager(_settings);

        public ISecretManager GetSecretManager(WebHostSettings settings)
        {
            return GetWebScriptHostManager(settings).SecretManager;
        }

        public WebScriptHostManager GetWebScriptHostManager() =>
            GetWebScriptHostManager(_settings);

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

        // TODO: FACAVAL (WEBHOOKS)
        //public WebHookReceiverManager GetWebHookReceiverManager(WebHostSettings settings)
        //{
        //    if (_activeReceiverManager != null)
        //    {
        //        return _activeReceiverManager;
        //    }

        //    lock (_syncLock)
        //    {
        //        EnsureInitialized(settings);

        //        return _activeReceiverManager ?? _standbyReceiverManager;
        //    }
        //}

        internal void EnsureInitialized(WebHostSettings settings)
        {
            if (!WebScriptHostManager.InStandbyMode)
            {
                // standby mode can only change from true to false
                // when standby mode changes, we reset all instances
                if (_activeHostManager == null)
                {
                    _settingsManager.Reset();

                    _activeScriptHostConfig = CreateScriptHostConfiguration(settings);
                    _activeHostManager = new WebScriptHostManager(_activeScriptHostConfig, _secretManagerFactory, _eventManager,  _settingsManager, settings, _router, _loggerFactoryBuilder);
                    // _activeReceiverManager = new WebHookReceiverManager(_activeHostManager.SecretManager);
                    InitializeFileSystem();

                    if (_standbyHostManager != null)
                    {
                        // we're starting the one and only one
                        // standby mode specialization
                        _activeScriptHostConfig.TraceWriter.Info(Resources.HostSpecializationTrace);

                        // After specialization, we need to ensure that custom timezone
                        // settings configured by the user (WEBSITE_TIME_ZONE) are honored.
                        // DateTime caches timezone information, so we need to clear the cache.
                        TimeZoneInfo.ClearCachedData();
                    }

                    if (_standbyHostManager != null)
                    {
                        _standbyHostManager.Stop();
                        _standbyHostManager.Dispose();
                    }
                    // _standbyReceiverManager?.Dispose();
                    _standbyScriptHostConfig = null;
                    _standbyHostManager = null;
                    // _standbyReceiverManager = null;
                }
            }
            else
            {
                if (_standbyHostManager == null)
                {

                    var standbySettings = CreateStandbySettings(settings);
                    _standbyScriptHostConfig = CreateScriptHostConfiguration(standbySettings, true);
                    _standbyHostManager = new WebScriptHostManager(_standbyScriptHostConfig, _secretManagerFactory, _eventManager, _settingsManager, standbySettings, _router, _loggerFactoryBuilder);
                    // _standbyReceiverManager = new WebHookReceiverManager(_standbyHostManager.SecretManager);

                    InitializeFileSystem();
                    StandbyManager.Initialize(_standbyScriptHostConfig);
                }
            }
        }

        internal static WebHostSettings CreateStandbySettings(WebHostSettings settings)
        {
            // we need to create a new copy of the settings to avoid modifying
            // the global settings
            // important that we use paths that are different than the configured paths
            // to ensure that placeholder files are isolated
            string tempRoot = Path.GetTempPath();
            var standbySettings = new WebHostSettings
            {
                LogPath = Path.Combine(tempRoot, @"Functions\Standby\Logs"),
                ScriptPath = Path.Combine(tempRoot, @"Functions\Standby\WWWRoot"),
                SecretsPath = Path.Combine(tempRoot, @"Functions\Standby\Secrets"),
                LoggerFactoryBuilder = settings.LoggerFactoryBuilder,
                TraceWriter = settings.TraceWriter,
                IsSelfHost = settings.IsSelfHost
            };

            return standbySettings;
        }

        internal static ScriptHostConfiguration CreateScriptHostConfiguration(WebHostSettings settings, bool inStandbyMode = false)
        {
            var scriptHostConfig = new ScriptHostConfiguration
            {
                RootScriptPath = settings.ScriptPath,
                RootLogPath = settings.LogPath,
                FileLoggingMode = FileLoggingMode.DebugOnly,
                TraceWriter = settings.TraceWriter,
                IsSelfHost = settings.IsSelfHost
            };

            if (inStandbyMode)
            {
                scriptHostConfig.FileLoggingMode = FileLoggingMode.DebugOnly;
                scriptHostConfig.HostConfig.StorageConnectionString = null;
                scriptHostConfig.HostConfig.DashboardConnectionString = null;
            }

            scriptHostConfig.HostConfig.HostId = Utility.GetDefaultHostId(_settingsManager, scriptHostConfig);

            return scriptHostConfig;
        }

        private static void InitializeFileSystem()
        {
            if (ScriptSettingsManager.Instance.IsAzureEnvironment)
            {
                Task.Run(() =>
                {
                    string home = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
                    if (!string.IsNullOrEmpty(home))
                    {
                        // Delete hostingstart.html if any. Azure creates that in all sites by default
                        string siteRootPath = Path.Combine(home, "site", "wwwroot");
                        string hostingStart = Path.Combine(siteRootPath, "hostingstart.html");
                        if (File.Exists(hostingStart))
                        {
                            File.Delete(hostingStart);
                        }

                        // Create the tools folder if it doesn't exist
                        string toolsPath = Path.Combine(home, "site", "tools");
                        Directory.CreateDirectory(toolsPath);

                        var folders = new List<string>();
                        folders.Add(Path.Combine(home, @"site", "tools"));

                        string path = Environment.GetEnvironmentVariable("PATH");
                        string additionalPaths = string.Join(";", folders);

                        // Make sure we haven't already added them. This can happen if the appdomain restart (since it's still same process)
                        if (!path.Contains(additionalPaths))
                        {
                            path = additionalPaths + ";" + path;

                            Environment.SetEnvironmentVariable("PATH", path);
                        }
                    }
                });
            }
        }

        public void Dispose()
        {
            _standbyHostManager?.Dispose();

            //_standbyReceiverManager?.Dispose();

            _activeHostManager?.Dispose();

            //_activeReceiverManager?.Dispose();
        }
    }
}