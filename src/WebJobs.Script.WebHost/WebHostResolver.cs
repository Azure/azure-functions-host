// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class WebHostResolver : IDisposable
    {
        private static ScriptSettingsManager _settingsManager;
        private static object _syncLock = new object();

        private readonly ISecretManagerFactory _secretManagerFactory;
        private readonly IScriptEventManager _eventManager;
        private readonly IWebJobsRouter _router;
        private readonly WebHostSettings _settings;
        private readonly ILoggerProviderFactory _loggerProviderFactory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IEventGenerator _eventGenerator;

        private ScriptHostConfiguration _standbyScriptHostConfig;
        private WebScriptHostManager _standbyHostManager;
        private ScriptHostConfiguration _activeScriptHostConfig;
        private WebScriptHostManager _activeHostManager;
        private Timer _specializationTimer;

        public WebHostResolver(ScriptSettingsManager settingsManager, ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager, WebHostSettings settings, IWebJobsRouter router, ILoggerProviderFactory loggerProviderFactory,
            ILoggerFactory loggerFactory, IEventGenerator eventGenerator)
        {
            _settingsManager = settingsManager;
            _secretManagerFactory = secretManagerFactory;
            _eventManager = eventManager;
            _router = router;
            _settings = settings;
            _eventGenerator = eventGenerator;

            // _loggerProviderFactory is used for creating the LoggerFactory for each ScriptHost
            _loggerProviderFactory = loggerProviderFactory;

            // _loggerFactory is used when there is no host available.
            _loggerFactory = loggerFactory;
        }

        public ILoggerFactory GetLoggerFactory(WebHostSettings settings)
        {
            // if we have an active initialized host, return it's fully configured
            // logger factory
            var hostManager = GetWebScriptHostManager(settings);
            if (hostManager.CanInvoke())
            {
                var config = GetScriptHostConfiguration(settings);
                return config.HostConfig.LoggerFactory;
            }
            else
            {
                // if there is no active host, return the default logger factory
                // By default, this will enable logging to the SystemLogger.
                return _loggerFactory;
            }
        }

        public ScriptHostConfiguration GetScriptHostConfiguration(WebHostSettings settings)
        {
            return GetActiveInstance(settings, ref _activeScriptHostConfig, ref _standbyScriptHostConfig);
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
            return GetActiveInstance(settings, ref _activeHostManager, ref _standbyHostManager);
        }

        /// <summary>
        /// This method ensures that all services managed by this class are initialized
        /// correctly taking into account specialization state transitions.
        /// </summary>
        internal void EnsureInitialized(WebHostSettings settings)
        {
            // Create a logger that we can use when the host isn't yet initialized.
            ILogger logger = _loggerFactory.CreateLogger(LogCategories.Startup);

            lock (_syncLock)
            {
                // Determine whether we should do normal or standby initialization
                if (!WebScriptHostManager.InStandbyMode)
                {
                    // We're not in standby mode. There are two cases to consider:
                    // 1) We _were_ in standby mode and now we're ready to specialize
                    // 2) We're doing non-specialization normal initialization
                    if (_activeHostManager == null &&
                        (_standbyHostManager == null || _settingsManager.ContainerReady))
                    {
                        _specializationTimer?.Dispose();
                        _specializationTimer = null;

                        _activeScriptHostConfig = CreateScriptHostConfiguration(settings);
                        _activeHostManager = new WebScriptHostManager(_activeScriptHostConfig, _secretManagerFactory, _eventManager, _settingsManager, settings,
                            _router, loggerProviderFactory: _loggerProviderFactory, loggerFactory: _loggerFactory, eventGenerator: _eventGenerator);
                        //_activeReceiverManager = new WebHookReceiverManager(_activeHostManager.SecretManager);
                        InitializeFileSystem(settings, _settingsManager.FileSystemIsReadOnly);

                        if (_standbyHostManager != null)
                        {
                            // we're starting the one and only one
                            // standby mode specialization
                            logger.LogInformation(Resources.HostSpecializationTrace);

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
                        //_standbyReceiverManager?.Dispose();
                        _standbyScriptHostConfig = null;
                        _standbyHostManager = null;
                        //_standbyReceiverManager = null;
                    }
                }
                else
                {
                    // We're in standby (placeholder) mode. Initialize the standby services.
                    if (_standbyHostManager == null)
                    {
                        var standbySettings = CreateStandbySettings(settings);
                        _standbyScriptHostConfig = CreateScriptHostConfiguration(standbySettings, true);
                        _standbyHostManager = new WebScriptHostManager(_standbyScriptHostConfig, _secretManagerFactory, _eventManager, _settingsManager, standbySettings,
                            _router, loggerProviderFactory: _loggerProviderFactory, loggerFactory: _loggerFactory, eventGenerator: _eventGenerator);
                        // _standbyReceiverManager = new WebHookReceiverManager(_standbyHostManager.SecretManager);

                        InitializeFileSystem(settings, _settingsManager.FileSystemIsReadOnly);
                        StandbyManager.Initialize(_standbyScriptHostConfig, logger);

                        // start a background timer to identify when specialization happens
                        // specialization usually happens via an http request (e.g. scale controller
                        // ping) but this timer is started as well to handle cases where we
                        // might not receive a request
                        _specializationTimer = new Timer(OnSpecializationTimerTick, settings, 1000, 1000);
                    }
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
                IsSelfHost = settings.IsSelfHost
            };

            return standbySettings;
        }

        internal static ScriptHostConfiguration CreateScriptHostConfiguration(WebHostSettings settings, bool inStandbyMode = false)
        {
            var scriptHostConfig = new ScriptHostConfiguration()
            {
                RootScriptPath = settings.ScriptPath,
                RootLogPath = settings.LogPath,
                FileLoggingMode = FileLoggingMode.DebugOnly,
                IsSelfHost = settings.IsSelfHost,
                TestDataPath = settings.TestDataPath
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

        /// <summary>
        /// Helper function used to manage active/standby transitions for objects managed
        /// by this class.
        /// </summary>
        private TInstance GetActiveInstance<TInstance>(WebHostSettings settings, ref TInstance activeInstance, ref TInstance standbyInstance)
        {
            if (activeInstance != null)
            {
                // if we have an active (specialized) instance, return it
                return activeInstance;
            }

            // we're either uninitialized or in standby mode
            // perform intitialization
            EnsureInitialized(settings);

            if (activeInstance != null)
            {
                return activeInstance;
            }
            else
            {
                return standbyInstance;
            }
        }

        private void OnSpecializationTimerTick(object state)
        {
            EnsureInitialized((WebHostSettings)state);

            // We know we've just specialized, since this timer only runs
            // when in standby mode. We want to initialize the host manager
            // immediately. Note that the host might also be initialized
            // concurrently by incoming http requests, but the initialization
            // here ensures that it takes place in the absence of any http
            // traffic.
            _activeHostManager?.EnsureHostStarted(CancellationToken.None);
        }

        private static void InitializeFileSystem(WebHostSettings settings, bool readOnlyFileSystem)
        {
            if (ScriptSettingsManager.Instance.IsAppServiceEnvironment)
            {
                // When running on App Service, we kick this off on the background
                Task.Run(() =>
                {
                    string home = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
                    if (!string.IsNullOrEmpty(home))
                    {
                        if (!readOnlyFileSystem)
                        {
                            // Delete hostingstart.html if any. Azure creates that in all sites by default
                            string siteRootPath = Path.Combine(home, @"site\wwwroot");
                            string hostingStart = Path.Combine(siteRootPath, "hostingstart.html");
                            if (File.Exists(hostingStart))
                            {
                                File.Delete(hostingStart);
                            }
                        }

                        if (!readOnlyFileSystem)
                        {
                            string toolsPath = Path.Combine(home, @"site\tools");
                            // Create the tools folder if it doesn't exist
                            Directory.CreateDirectory(toolsPath);

                            // Create the test data folder
                            Directory.CreateDirectory(settings.TestDataPath);
                        }

                        var folders = new List<string>();
                        folders.Add(Path.Combine(home, @"site\tools"));

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
            else
            {
                // Ensure we have our scripts directory in non-Azure scenarios
                if (!string.IsNullOrEmpty(settings.ScriptPath))
                {
                    Directory.CreateDirectory(settings.ScriptPath);
                }

                if (!string.IsNullOrEmpty(settings.TestDataPath))
                {
                    Directory.CreateDirectory(settings.TestDataPath);
                }
            }
        }

        public void Dispose()
        {
            _specializationTimer?.Dispose();
            _standbyHostManager?.Dispose();

            //_standbyReceiverManager?.Dispose();

            _activeHostManager?.Dispose();

            //_activeReceiverManager?.Dispose();
        }
    }
}
