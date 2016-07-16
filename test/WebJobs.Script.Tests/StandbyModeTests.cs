// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyModeTests
    {
        [Fact]
        public void StanbyMode_Getter_Default()
        {
            using (new TestEnvironment())
            {
                // initially false
                Assert.Equal(false, ScriptHost.InStandbyMode);
            }
        }

        [Fact]
        public void StanbyMode_Getter_WarmUp()
        {
            using (new TestEnvironment())
            {
                Environment.SetEnvironmentVariable("WEBSITE_PLACEHOLDER_MODE", "1");
                Assert.Equal(true, ScriptHost.InStandbyMode);

                Environment.SetEnvironmentVariable("WEBSITE_PLACEHOLDER_MODE", "0");
                Assert.Equal(false, ScriptHost.InStandbyMode);

                // test only set one way
                Environment.SetEnvironmentVariable("WEBSITE_PLACEHOLDER_MODE", "1");
                Assert.Equal(false, ScriptHost.InStandbyMode);
            }
        }

        [Fact]
        public void StanbyMode_GetScriptHostConfiguration()
        {
            StanbyMode_GetInstance(WebHostResolver.GetScriptHostConfiguration);
        }

        [Fact]
        public void StanbyMode_GetSecretManager()
        {
            StanbyMode_GetInstance(WebHostResolver.GetSecretManager);
        }

        [Fact]
        public void StanbyMode_GetWebHookReceiverManager()
        {
            StanbyMode_GetInstance(WebHostResolver.GetWebHookReceiverManager);
        }

        [Fact]
        public void StanbyMode_GetWebScriptHostManager()
        {
            StanbyMode_GetInstance(WebHostResolver.GetWebScriptHostManager);
        }

        private void StanbyMode_GetInstance<T>(Func<WebHostSettings, T> func)
        {
            using (new TestEnvironment())
            {
                T prev = default(T);
                T current = default(T);
                T next = default(T);
                try
                {
                    Environment.SetEnvironmentVariable("WEBSITE_PLACEHOLDER_MODE", "1");

                    var settings = GetWebHostSettings();
                    prev = func(settings);
                    Assert.NotNull(prev);

                    Environment.SetEnvironmentVariable("WEBSITE_PLACEHOLDER_MODE", "0");
                    current = func(settings);
                    Assert.NotNull(current);
                    Assert.NotSame(prev, current);

                    // test only set one way
                    Environment.SetEnvironmentVariable("WEBSITE_PLACEHOLDER_MODE", "1");
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
        public void StanbyMode_WarmupTest()
        {
            using (new TestEnvironment())
            {
                var settings = GetWebHostSettings();
                WebScriptHostManager.WarmUp(settings);

                var hostLogPath = Path.Combine(settings.LogPath, @"host");
                var hostLogFile = Directory.GetFiles(hostLogPath).First();
                var content = File.ReadAllText(hostLogFile);

                Assert.Contains("Warm up started", content);
                Assert.Contains("Warm up succeeded", content);
            }
        }

        private WebHostSettings GetWebHostSettings()
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(home, @"site\wwwroot"),
                LogPath = Path.Combine(home, @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(home, @"data\Functions\secrets")
            };
        }

        private class TestEnvironment : IDisposable
        {
            private string _home;
            private string _prevHome;

            public TestEnvironment()
            {
                _prevHome = Environment.GetEnvironmentVariable("HOME");

                _home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(_home);
                Environment.SetEnvironmentVariable("HOME", _home);

                Reset();
            }

            public void Dispose()
            {
                Reset();

                Environment.SetEnvironmentVariable("HOME", _prevHome);
                Directory.Delete(_home, recursive: true);
            }

            private void Reset()
            {
                ScriptHost.ResetStandbyMode();
                WebHostResolver.Reset();
                Environment.SetEnvironmentVariable("WEBSITE_PLACEHOLDER_MODE", null);
            }
        }
    }
}