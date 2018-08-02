﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.StandbyModeTests)]
    public class StandbyModeTests : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        public StandbyModeTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            var eventManagerMock = new Mock<IScriptEventManager>();
            var routerMock = new Mock<IWebJobsRouter>();

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            Mock<IEventGenerator> eventGeneratorMock = new Mock<IEventGenerator>();
        }

        [Fact]
        public void InStandbyMode_ReturnsExpectedValue()
        {
            using (new TestEnvironment())
            {
                // initially false
                Assert.Equal(false, new ScriptWebHostEnvironment().InStandbyMode);
            }

            using (new TestEnvironment())
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.Equal(true, new ScriptWebHostEnvironment().InStandbyMode);

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                Assert.Equal(false, new ScriptWebHostEnvironment().InStandbyMode);

                // test only set one way
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.Equal(false, new ScriptWebHostEnvironment().InStandbyMode);
            }
        }

        [Fact(Skip = "Review test")]
        public async Task GetScriptHostConfiguration_ReturnsExpectedValue()
        {
            // TODO: DI (FACAVAL) Logic here has changed significantly - Mathewc - please review
            //await TestGetter(_webHostResolver.GetScriptHostConfiguration);
        }

        [Fact(Skip = "Review test")]
        public async Task GetSecretManager_ReturnsExpectedValue()
        {
            // TODO: DI (FACAVAL) Logic here has changed significantly - Mathewc - please review
            //await TestGetter(_webHostResolver.GetSecretManager);
        }

        [Fact(Skip = "Review test")]
        public async Task GetWebScriptHostManager_ReturnsExpectedValue()
        {
            // TODO: DI (FACAVAL) Logic here has changed significantly - Mathewc - please review
            //await TestGetter(_webHostResolver.GetWebScriptHostManager);
        }

        [Fact(Skip = "Review test")]
        public async Task EnsureInitialized_NonPlaceholderMode()
        {
            using (new TestEnvironment())
            {
                // TODO: DI (FACAVAL) Logic here has changed significantly - Mathewc - please review
                var settings = await GetWebHostSettings();
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                Assert.False(new ScriptWebHostEnvironment().InStandbyMode);
                //_webHostResolver.EnsureInitialized(settings);

                // ensure specialization message is NOT written
                var traces = _loggerProvider.GetAllLogMessages().ToArray();
                var traceEvent = traces.SingleOrDefault(p => p.FormattedMessage.Contains(Resources.HostSpecializationTrace));
                Assert.Null(traceEvent);
            }
        }

        [Fact(Skip = "Review test")]
        public async Task EnsureInitialized_PlaceholderMode()
        {
            // TODO: DI (FACAVAL) Logic here has changed significantly - Mathewc - please review
            //using (new TestEnvironment())
            //{
            //    var settings = await GetWebHostSettings();
            //    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            //    Assert.True(WebScriptHostManager.InStandbyMode);
            //    _webHostResolver.EnsureInitialized(settings);

            //    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            //    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            //    Assert.False(WebScriptHostManager.InStandbyMode);
            //    _webHostResolver.EnsureInitialized(settings);

            //    var traces = _loggerProvider.GetAllLogMessages().ToArray();
            //    var traceEvent = traces.Last();
            //    Assert.Equal(Resources.HostSpecializationTrace, traceEvent.FormattedMessage);
            //    Assert.Equal(LogLevel.Information, traceEvent.Level);
            //}
        }

        private async Task TestGetter<T>(Func<ScriptWebHostOptions, T> func)
        {
            using (new TestEnvironment())
            {
                T prev = default(T);
                T current = default(T);
                T next = default(T);
                try
                {
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

                    var settings = await GetWebHostSettings();
                    prev = func(settings);
                    Assert.NotNull(prev);

                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
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

        private Task<ScriptWebHostOptions> GetWebHostSettings()
        {
            var home = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
            var settings = new ScriptWebHostOptions
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(home, @"site\wwwroot"),
                LogPath = Path.Combine(home, @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(home, @"data\Functions\secrets")
            };

            Directory.CreateDirectory(settings.ScriptPath);
            Directory.CreateDirectory(settings.LogPath);
            Directory.CreateDirectory(settings.SecretsPath);

            return Task.Delay(200)
                .ContinueWith(t => settings);
        }

        public void Dispose()
        {
            
        }

        private class TestEnvironment : IDisposable
        {
            private readonly ScriptSettingsManager _settingsManager;
            private string _home;
            private string _prevHome;
            private string _prevPlaceholderMode;
            private string _prevInstanceId;

            public TestEnvironment()
            {
                _settingsManager = ScriptSettingsManager.Instance;
                _prevHome = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
                _prevPlaceholderMode = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode);
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
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, null);
            }
        }
    }
}