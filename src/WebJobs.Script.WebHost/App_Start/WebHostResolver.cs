// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class WebHostResolver : IDisposable
    {
        private static ScriptSettingsManager _settingsManager;
        private static object _syncLock = new object();

        private readonly ISecretManagerFactory _secretManagerFactory;
        private readonly IScriptEventManager _eventManager;
        private ScriptHostConfiguration _standbyScriptHostConfig;
        private WebScriptHostManager _standbyHostManager;
        private WebHookReceiverManager _standbyReceiverManager;
        private ScriptHostConfiguration _activeScriptHostConfig;
        private WebScriptHostManager _activeHostManager;
        private WebHookReceiverManager _activeReceiverManager;
        private ILoggerFactory _defaultLoggerFactory;
        private TraceWriter _defaultTraceWriter;
        private Timer _specializationTimer;

        public WebHostResolver(ScriptSettingsManager settingsManager, ISecretManagerFactory secretManagerFactory, IScriptEventManager eventManager)
        {
            _settingsManager = settingsManager;
            _secretManagerFactory = secretManagerFactory;
            _eventManager = eventManager;
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
                if (_defaultLoggerFactory == null)
                {
                    lock (_syncLock)
                    {
                        if (_defaultLoggerFactory == null)
                        {
                            _defaultLoggerFactory = CreateDefaultLoggerFactory(settings);
                        }
                    }
                }
                
                return _defaultLoggerFactory;
            }
        }

        public TraceWriter GetTraceWriter(WebHostSettings settings)
        {
            // if we have an active initialized host, return it's fully configured
            // trace writer
            var hostManager = GetWebScriptHostManager(settings);
            if (hostManager.CanInvoke())
            {
                return hostManager.Instance.TraceWriter;
            }
            else
            {
                // if there is no active host, return the default trace writer
                if (_defaultTraceWriter == null)
                {
                    lock (_syncLock)
                    {
                        if (_defaultTraceWriter == null)
                        {
                            _defaultTraceWriter = CreateDefaultTraceWriter(settings);
                        }
                    }
                }

                return _defaultTraceWriter;
            }
        }

        public ISwaggerDocumentManager GetSwaggerDocumentManager(WebHostSettings settings)
        {
            return GetWebScriptHostManager(settings).SwaggerDocumentManager;
        }

        public ScriptHostConfiguration GetScriptHostConfiguration(WebHostSettings settings)
        {
            return GetActiveInstance(settings, ref _activeScriptHostConfig, ref _standbyScriptHostConfig);
        }

        public ISecretManager GetSecretManager(WebHostSettings settings)
        {
            return GetWebScriptHostManager(settings).SecretManager;
        }

        public WebScriptHostManager GetWebScriptHostManager(WebHostSettings settings)
        {
            return GetActiveInstance(settings, ref _activeHostManager, ref _standbyHostManager);
        }

        public WebHookReceiverManager GetWebHookReceiverManager(WebHostSettings settings)
        {
            return GetActiveInstance(settings, ref _activeReceiverManager, ref _standbyReceiverManager);
        }

        /// <summary>
        /// This method ensures that all services managed by this class are initialized
        /// correctly taking into account specialization state transitions.
        /// </summary>
        internal void EnsureInitialized(WebHostSettings settings)
        {
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
                        _settingsManager.Reset();
                        _specializationTimer?.Dispose();
                        _specializationTimer = null;

                        _activeScriptHostConfig = CreateScriptHostConfiguration(settings);
                        _activeHostManager = new WebScriptHostManager(_activeScriptHostConfig, _secretManagerFactory, _eventManager, _settingsManager, settings);
                        _activeReceiverManager = new WebHookReceiverManager(_activeHostManager.SecretManager);
                        InitializeFileSystem(_settingsManager.FileSystemIsReadOnly);

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
                        _standbyReceiverManager?.Dispose();
                        _standbyScriptHostConfig = null;
                        _standbyHostManager = null;
                        _standbyReceiverManager = null;
                    }
                }
                else
                {
                    // We're in standby (placeholder) mode. Initialize the standby services.
                    if (_standbyHostManager == null)
                    {
                        var standbySettings = CreateStandbySettings(settings);
                        _standbyScriptHostConfig = CreateScriptHostConfiguration(standbySettings, true);
                        _standbyHostManager = new WebScriptHostManager(_standbyScriptHostConfig, _secretManagerFactory, _eventManager, _settingsManager, standbySettings);
                        _standbyReceiverManager = new WebHookReceiverManager(_standbyHostManager.SecretManager);

                        InitializeFileSystem(_settingsManager.FileSystemIsReadOnly);
                        StandbyManager.Initialize(_standbyScriptHostConfig);

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
                IsSelfHost = settings.IsSelfHost,
                LoggerFactoryBuilder = settings.LoggerFactoryBuilder
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

        private TraceWriter CreateDefaultTraceWriter(WebHostSettings settings)
        {
            // need to set up a default trace writer that logs to system logs
            var config = GetScriptHostConfiguration(settings);
            var systemEventGenerator = config.HostConfig.GetService<IEventGenerator>() ?? new EventGenerator();
            TraceWriter systemTraceWriter = new SystemTraceWriter(systemEventGenerator, _settingsManager, TraceLevel.Verbose);

            return systemTraceWriter;
        }

        private ILoggerFactory CreateDefaultLoggerFactory(WebHostSettings settings)
        {
            var loggerFactory = new LoggerFactory();
            if (!string.IsNullOrEmpty(_settingsManager.ApplicationInsightsInstrumentationKey))
            {
                var config = GetScriptHostConfiguration(settings);
                var clientFactory = new ScriptTelemetryClientFactory(_settingsManager.ApplicationInsightsInstrumentationKey, config.ApplicationInsightsSamplingSettings, config.LogFilter.Filter);
                loggerFactory.AddApplicationInsights(clientFactory);
            }

            return loggerFactory;
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
            // concurrently  by incoming http requests, but the initialization
            // here ensures that it takes place in the absence of any http
            // traffic.
            _activeHostManager?.EnsureInitialized();
        }

        private static void InitializeFileSystem(bool readOnlyFileSystem)
        {
            if (ScriptSettingsManager.Instance.IsAzureEnvironment)
            {
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

                        string toolsPath = Path.Combine(home, @"site\tools");
                        if (!readOnlyFileSystem)
                        {
                            // Create the tools folder if it doesn't exist
                            Directory.CreateDirectory(toolsPath);
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
        }

        public void Dispose()
        {
            _specializationTimer?.Dispose();
            _standbyHostManager?.Dispose();
            _standbyReceiverManager?.Dispose();

            _activeHostManager?.Dispose();
            _activeReceiverManager?.Dispose();
            ((IDisposable)_defaultTraceWriter)?.Dispose();
            _defaultLoggerFactory?.Dispose();
        }
    }
}