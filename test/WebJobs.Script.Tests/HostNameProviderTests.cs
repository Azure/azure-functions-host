// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HostNameProviderTests : IDisposable
    {
        private readonly Mock<ScriptSettingsManager> _mockSettings;

        public HostNameProviderTests()
        {
            _mockSettings = new Mock<ScriptSettingsManager>(MockBehavior.Strict);
            ScriptSettingsManager.Instance = _mockSettings.Object;
            HostNameProvider.Reset();
        }

        [Theory]
        [InlineData("test.azurewebsites.net", "test", "test.azurewebsites.net")]
        [InlineData(null, "test", "test.azurewebsites.net")]
        [InlineData("", "test", "test.azurewebsites.net")]
        [InlineData(null, null, null)]
        [InlineData("", "", "")]
        public void GetValue_ReturnsExpectedResult(string hostName, string siteName, string expected)
        {
            _mockSettings.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(hostName);
            _mockSettings.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsiteName)).Returns(siteName);

            Assert.Equal(expected, HostNameProvider.Value);
        }

        [Fact]
        public void Synchronize_UpdatesValue()
        {
            _mockSettings.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsiteHostName)).Returns((string)null);
            _mockSettings.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsiteName)).Returns((string)null);

            Assert.Equal(null, HostNameProvider.Value);

            // no header present
            var request = new HttpRequestMessage();
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            HostNameProvider.Synchronize(request, traceWriter);
            Assert.Equal(null, HostNameProvider.Value);

            // empty header value
            request = new HttpRequestMessage();
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, string.Empty);
            HostNameProvider.Synchronize(request, traceWriter);
            Assert.Equal(null, HostNameProvider.Value);

            // host provided via header - expect update
            request = new HttpRequestMessage();
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test.azurewebsites.net");
            HostNameProvider.Synchronize(request, traceWriter);
            Assert.Equal("test.azurewebsites.net", HostNameProvider.Value);
            var logs = traceWriter.GetTraces();
            Assert.Equal(1, logs.Count);
            Assert.Equal("HostName updated from '' to 'test.azurewebsites.net'", logs.Single().Message);

            // no change in header value - no update expected
            traceWriter.ClearTraces();
            HostNameProvider.Synchronize(request, traceWriter);
            Assert.Equal("test.azurewebsites.net", HostNameProvider.Value);
            logs = traceWriter.GetTraces();
            Assert.Equal(0, logs.Count);

            // another change - expect update
            request = new HttpRequestMessage();
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test2.azurewebsites.net");
            HostNameProvider.Synchronize(request, traceWriter);
            Assert.Equal("test2.azurewebsites.net", HostNameProvider.Value);
            logs = traceWriter.GetTraces();
            Assert.Equal(1, logs.Count);
            Assert.Equal("HostName updated from 'test.azurewebsites.net' to 'test2.azurewebsites.net'", logs.Single().Message);
        }

        public void Dispose()
        {
            ScriptSettingsManager.Instance = new ScriptSettingsManager();
        }
    }
}