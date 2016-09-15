// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyModeTests : IDisposable
    {
        private readonly WebHostResolver _webHostResolver;

        public StandbyModeTests()
        {
            _webHostResolver = new WebHostResolver();
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
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.Equal(true, WebScriptHostManager.InStandbyMode);

                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                Assert.Equal(false, WebScriptHostManager.InStandbyMode);

                // test only set one way
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
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
        public void GetWebHookReceiverManager_ReturnsExpectedValue()
        {
            TestGetter(_webHostResolver.GetWebHookReceiverManager);
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
                    Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

                    var settings = GetWebHostSettings();
                    prev = func(settings);
                    Assert.NotNull(prev);

                    Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                    current = func(settings);
                    Assert.NotNull(current);
                    Assert.NotSame(prev, current);

                    // test only set one way
                    Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
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
                WebScriptHostManager.WarmUp(settings);

                var hostLogPath = Path.Combine(settings.LogPath, @"host");
                var hostLogFile = Directory.GetFiles(hostLogPath).First();
                var content = File.ReadAllText(hostLogFile);

                Assert.Contains("Warm up started", content);
                Assert.Contains("Executed: 'Functions.Test-CSharp' (Succeeded)", content);
                Assert.Contains("Executed: 'Functions.Test-FSharp' (Succeeded)", content);
                Assert.Contains("Warm up succeeded", content);
            }
        }

        private WebHostSettings GetWebHostSettings()
        {
            var home = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(home, @"site\wwwroot"),
                LogPath = Path.Combine(home, @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(home, @"data\Functions\secrets")
            };
        }

        public void Dispose()
        {
            _webHostResolver?.Dispose();
        }

        private class TestEnvironment : IDisposable
        {
            private string _home;
            private string _prevHome;

            public TestEnvironment()
            {
                _prevHome = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);

                _home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(_home);
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, _home);

                Reset();
            }

            public void Dispose()
            {
                Reset();

                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, _prevHome);
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
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, null);
            }
        }
    }
}