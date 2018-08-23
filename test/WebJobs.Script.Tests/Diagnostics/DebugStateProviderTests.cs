// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class DebugStateProviderTests
    {
        private readonly ScriptApplicationHostOptions _options;
        private readonly TestChangeTokenSource _tokenSource;
        private readonly DebugStateProvider _provider;

        public DebugStateProviderTests()
        {
            _options = new ScriptApplicationHostOptions
            {
                LogPath = Path.Combine(Directory.GetCurrentDirectory(), "DebugPath1")
            };

            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(_options);
            _tokenSource = new TestChangeTokenSource();
            var changeTokens = new[] { _tokenSource };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);

            _provider = new DebugStateProvider(optionsMonitor, new ScriptEventManager());
        }

        [Fact]
        public void LastDebugNotify_IsRefreshed_OnOptionsChange()
        {
            // Put a file in "DebugPath2"
            var newLogPath = Path.Combine(Directory.GetCurrentDirectory(), "DebugPath2");
            var newHostLogPath = Path.Combine(newLogPath, "Host");

            try
            {
                Directory.CreateDirectory(newHostLogPath);
                File.WriteAllText(Path.Combine(newHostLogPath, ScriptConstants.DebugSentinelFileName), string.Empty);

                Assert.Equal(DateTime.MinValue, _provider.LastDebugNotify);
                Assert.False(_provider.InDebugMode);

                _options.LogPath = newLogPath;
                _tokenSource.SignalChange();

                Assert.Equal(DateTime.UtcNow, _provider.LastDebugNotify, TimeSpan.FromMinutes(5));
                Assert.True(_provider.InDebugMode);
            }
            finally
            {
                Directory.Delete(newLogPath, true);
            }
        }
    }
}
