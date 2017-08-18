// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
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
            var routerMock = new Mock<IWebJobsRouter>();

            _webHostResolver = new WebHostResolver(_settingsManager, new TestSecretManagerFactory(false),
                eventManagerMock.Object, WebHostSettings.CreateDefault(_settingsManager), routerMock.Object, new DefaultLoggerFactoryBuilder());
        }

        [Fact]
        public void InStandbyMode_ReturnsExpectedValue()
        {
            using (new TestEnvironment())
            {
                // initially false
                Assert.False(WebScriptHostManager.InStandbyMode);
            }

            using (new TestEnvironment())
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.True(WebScriptHostManager.InStandbyMode);

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                Assert.False(WebScriptHostManager.InStandbyMode);

                // test only set one way
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.False(WebScriptHostManager.InStandbyMode);
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

        // TODO: FACAVAL - Still needed?
        //[Fact]
        //public void GetSwaggerDocumentManager_ReturnsExpectedValue()
        //{
        //    TestGetter(_webHostResolver.GetSwaggerDocumentManager);
        //}

        //[Fact]
        //public void GetWebHookReceiverManager_ReturnsExpectedValue()
        //{
        //    TestGetter(_webHostResolver.GetWebHookReceiverManager);
        //}

        [Fact]
        public void GetPerformanceManager_ReturnsExpectedValue()
        {
            TestGetter(_webHostResolver.GetPerformanceManager);
        }

        [Fact]
        public void GetWebScriptHostManager_ReturnsExpectedValue()
        {
            TestGetter(_webHostResolver.GetWebScriptHostManager);
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
                    _traceWriter.Traces.Clear();

                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

                    var settings = GetWebHostSettings();
                    prev = func(settings);
                    Assert.NotNull(prev);

                    _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                    current = func(settings);
                    Assert.NotNull(current);
                    Assert.NotSame(prev, current);

                    var traces = _traceWriter.Traces.ToArray();
                    var traceEvent = traces.Last();
                    Assert.Equal("Host has been specialized", traceEvent.Message);
                    Assert.Equal(TraceLevel.Info, traceEvent.Level);

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

        [Fact]
        public void Warmup_Succeeds()
        {
            using (new TestEnvironment())
            {
                var settings = GetWebHostSettings();
                var eventManagerMock = new Mock<IScriptEventManager>();
                WebScriptHostManager.WarmUp(settings, eventManagerMock.Object);

                var hostLogPath = Path.Combine(settings.LogPath, @"host");
                var hostLogFile = Directory.GetFiles(hostLogPath).First();
                var content = File.ReadAllText(hostLogFile);

                Assert.Contains("Warm up started", content);
                Assert.Contains("Executed 'Functions.Test-CSharp' (Succeeded, Id=", content);
                Assert.Contains("Warm up succeeded", content);
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
                WebScriptHostManager.ResetStandbyMode();
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, null);
            }
        }
    }
}