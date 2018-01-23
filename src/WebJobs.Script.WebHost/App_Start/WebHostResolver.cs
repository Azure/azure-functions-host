﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
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
        private static ILoggerFactory _emptyLoggerFactory = new LoggerFactory();
        private static object _syncLock = new object();

        private readonly ISecretManagerFactory _secretManagerFactory;
        private readonly IScriptEventManager _eventManager;
        private ScriptHostConfiguration _standbyScriptHostConfig;
        private WebScriptHostManager _standbyHostManager;
        private WebHookReceiverManager _standbyReceiverManager;
        private ScriptHostConfiguration _activeScriptHostConfig;
        private WebScriptHostManager _activeHostManager;
        private WebHookReceiverManager _activeReceiverManager;
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
            WebScriptHostManager manager = GetWebScriptHostManager(settings);

            if (!manager.CanInvoke())
            {
                // The host is still starting and the LoggerFactory cannot be used. Return
                // an empty LoggerFactory rather than waiting. This will mostly affect
                // calls to /admin/host/status, which do not block on CanInvoke. It allows this
                // API to remain fast with the side effect of occassional missed logs.
                return _emptyLoggerFactory;
            }

            return GetScriptHostConfiguration(settings).HostConfig.LoggerFactory;
        }

        public TraceWriter GetTraceWriter(WebHostSettings settings)
        {
            // if we have an active host, return it's fully configured
            // trace writer
            var hostManager = GetWebScriptHostManager(settings);
            var traceWriter = hostManager.Instance?.TraceWriter;

            if (traceWriter != null)
            {
                return traceWriter;
            }

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
                if (!WebScriptHostManager.InStandbyMode)
                {
                    // standby mode can only change from true to false
                    // when standby mode changes, we reset all instances
                    if (_activeHostManager == null)
                    {
                        _settingsManager.Reset();
                        _specializationTimer?.Dispose();
                        _specializationTimer = null;

                        _activeScriptHostConfig = CreateScriptHostConfiguration(settings);
                        _activeHostManager = new WebScriptHostManager(_activeScriptHostConfig, _secretManagerFactory, _eventManager, _settingsManager, settings);
                        _activeReceiverManager = new WebHookReceiverManager(_activeHostManager.SecretManager);
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
                        _standbyReceiverManager?.Dispose();
                        _standbyScriptHostConfig = null;
                        _standbyHostManager = null;
                        _standbyReceiverManager = null;
                    }
                }
                else
                {
                    // we're in standby (placeholder) mode
                    if (_standbyHostManager == null)
                    {
                        var standbySettings = CreateStandbySettings(settings);
                        _standbyScriptHostConfig = CreateScriptHostConfiguration(standbySettings, true);
                        _standbyHostManager = new WebScriptHostManager(_standbyScriptHostConfig, _secretManagerFactory, _eventManager, _settingsManager, standbySettings);
                        _standbyReceiverManager = new WebHookReceiverManager(_standbyHostManager.SecretManager);

                        InitializeFileSystem();
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
            // need to set up a default trace writer that logs both host logs and system logs
            var config = GetScriptHostConfiguration(settings);
            var systemEventGenerator = config.HostConfig.GetService<IEventGenerator>() ?? new EventGenerator();
            TraceWriter systemTraceWriter = new SystemTraceWriter(systemEventGenerator, _settingsManager, TraceLevel.Verbose);

            // Note that we're creating a logger here that is independent of log configuration settings
            // since we haven't read config yet. This logger is independent on the host having been started.
            // That does mean that even if file logging is disabled, some logs might get written to the file system
            // but that's ok.
            string hostLogFilePath = Path.Combine(config.RootLogPath, "Host");
            TraceWriter fileTraceWriter = new FileTraceWriter(hostLogFilePath, config.HostConfig.Tracing.ConsoleLevel);

            return new CompositeTraceWriter(new[] { systemTraceWriter, fileTraceWriter });
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
                        string siteRootPath = Path.Combine(home, @"site\wwwroot");
                        string hostingStart = Path.Combine(siteRootPath, "hostingstart.html");
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
            _standbyReceiverManager?.Dispose();

            _activeHostManager?.Dispose();
            _activeReceiverManager?.Dispose();
            _specializationTimer?.Dispose();
            ((IDisposable)_defaultTraceWriter)?.Dispose();
        }
    }
}