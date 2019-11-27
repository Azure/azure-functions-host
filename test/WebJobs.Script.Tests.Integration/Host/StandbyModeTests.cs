﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyModeTests : IDisposable
    {
        private readonly WebHostResolver _webHostResolver;
        private readonly TestTraceWriter _traceWriter;
        private readonly ScriptSettingsManager _settingsManager;

        public StandbyModeTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            var eventManagerMock = new Mock<IScriptEventManager>();

            _webHostResolver = new WebHostResolver(_settingsManager, new TestSecretManagerFactory(false), eventManagerMock.Object);
            _traceWriter = new TestTraceWriter(TraceLevel.Info);

            WebScriptHostManager.ResetStandbyMode();
        }

        [Fact]
        public void InStandbyMode_ReturnsExpectedValue_AzureWebsitePlaceholderMode_Set()
        {
            using (new TestEnvironment())
            {
                // initially false
                Assert.Equal(false, WebScriptHostManager.InStandbyMode);
            }

            using (new TestEnvironment())
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.Equal(true, WebScriptHostManager.InStandbyMode);
            }
        }

        [Fact]
        public void InStandbyMode_ReturnsExpectedValue_AzureWebsiteContainerReady_Set()
        {
            using (new TestEnvironment())
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                Assert.Equal(true, WebScriptHostManager.InStandbyMode);
            }
        }

        [Fact]
        public void InStandbyMode_ReturnsExpectedValue()
        {
            using (new TestEnvironment())
            {
                // initially false
                Assert.Equal(false, WebScriptHostManager.InStandbyMode);
            }

            using (new TestEnvironment())
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteConfigurationReady, "1");
                Assert.Equal(false, WebScriptHostManager.InStandbyMode);

                // test only set one way
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.Equal(false, WebScriptHostManager.InStandbyMode);
            }
        }

        [Fact]
        public void GetScriptHostConfiguration_ReturnsExpectedValue()
        {
            TestGetter(_webHostResolver.GetScriptHostConfiguration);
        }

        [Fact]
        public void GetSecretManager_ReturnsExpectedValue()
        {
            TestGetter(_webHostResolver.GetSecretManager);
        }

        [Fact]
        public void GetSwaggerDocumentManager_ReturnsExpectedValue()
        {
            TestGetter(_webHostResolver.GetSwaggerDocumentManager);
        }

        [Fact]
        public void GetWebHookReceiverManager_ReturnsExpectedValue()
        {
            TestGetter(_webHostResolver.GetWebHookReceiverManager);
        }

        [Fact]
        public void GetWebScriptHostManager_ReturnsExpectedValue()
        {
            TestGetter(_webHostResolver.GetWebScriptHostManager);
        }

        [Fact]
        public void EnsureInitialized_NonPlaceholderMode()
        {
            using (new TestEnvironment())
            {
                _traceWriter.ClearTraces();

                var settings = GetWebHostSettings();
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteConfigurationReady, "1");
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");

                Assert.False(WebScriptHostManager.InStandbyMode);
                _webHostResolver.EnsureInitialized(settings);

                // ensure specialization message is NOT written
                var traces = _traceWriter.GetTraces();
                var traceEvent = traces.SingleOrDefault(p => p.Message.Contains(Resources.HostSpecializationTrace));
                Assert.Null(traceEvent);

                // Verify Host Initilization log is written
                Assert.True(traces.Any(m => m.Message == "Host initializing."));
            }
        }

        [Fact]
        public void EnsureInitialized_PlaceholderMode()
        {
            using (new TestEnvironment())
            {
                _traceWriter.ClearTraces();

                var settings = GetWebHostSettings();
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.True(WebScriptHostManager.InStandbyMode);
                _webHostResolver.EnsureInitialized(settings);

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteConfigurationReady, "1");
                Assert.False(WebScriptHostManager.InStandbyMode);
                _webHostResolver.EnsureInitialized(settings);

                var traces = _traceWriter.GetTraces();
                Assert.True(traces.Count() == 6);
                Assert.True(traces.Any(m => m.Message == "Host initializing."));
                var traceEvent = traces.Last();
                Assert.Equal(Resources.HostSpecializationTrace, traceEvent.Message);
                Assert.Equal(TraceLevel.Info, traceEvent.Level);
            }
        }

        private void TestGetter<T>(Func<WebHostSettings, T> func)
        {
            using (new TestEnvironment())
            {
                T prev = default(T);
                T current = default(T);
                T next = default(T);
                try
                {
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

                    var settings = GetWebHostSettings();
                    prev = func(settings);
                    Assert.NotNull(prev);

                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteConfigurationReady, "1");
                    current = func(settings);
                    Assert.NotNull(current);
                    Assert.NotSame(prev, current);

                    // test only set one way
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                    next = func(settings);
                    Assert.NotNull(next);
                    Assert.Same(next, current);
                }
                finally
                {
                    (prev as IDisposable)?.Dispose();
                    (current as IDisposable)?.Dispose();
                    (next as IDisposable)?.Dispose();
                }
            }
        }

        private WebHostSettings GetWebHostSettings()
        {
            var home = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(home, @"site\wwwroot"),
                LogPath = Path.Combine(home, @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(home, @"data\Functions\secrets"),
                TraceWriter = _traceWriter
            };
        }

        public void Dispose()
        {
            _webHostResolver?.Dispose();
        }

        private class TestEnvironment : IDisposable
        {
            private readonly ScriptSettingsManager _settingsManager;
            private string _home;
            private string _prevHome;
            private string _prevPlaceholderMode;
            private string _prevConfigurationReady;
            private string _prevContainerReady;
            private string _prevInstanceId;

            public TestEnvironment()
            {
                _settingsManager = ScriptSettingsManager.Instance;
                _prevHome = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
                _prevPlaceholderMode = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode);
                _prevConfigurationReady = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteConfigurationReady);
                _prevContainerReady = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady);
                _prevInstanceId = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId);

                _home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(_home);
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteHomePath, _home);

                Reset();
            }

            public void Dispose()
            {
                Reset();

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteHomePath, _prevHome);
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId, _prevInstanceId);
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, _prevPlaceholderMode);
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteConfigurationReady, _prevConfigurationReady);
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, _prevContainerReady);

                try
                {
                    Directory.Delete(_home, recursive: true);
                }
                catch
                {
                    // best effort
                }
            }

            private void Reset()
            {
                WebScriptHostManager.ResetStandbyMode();
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, null);
            }
        }
    }
}