// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HostNameProviderTests
    {
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly HostNameProvider _hostNameProvider;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly ILogger _logger;

        public HostNameProviderTests()
        {
            _mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);

            _loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _logger = loggerFactory.CreateLogger<HostNameProvider>();
            _hostNameProvider = new HostNameProvider(_mockEnvironment.Object);
        }

        [Theory]
        [InlineData("test.azurewebsites.net", "test", "test.azurewebsites.net")]
        [InlineData(null, "test", "test.azurewebsites.net")]
        [InlineData("", "test", "test.azurewebsites.net")]
        [InlineData(null, null, null)]
        [InlineData("", "", "")]
        public void GetValue_ReturnsExpectedResult(string hostName, string siteName, string expected)
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(hostName);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns(siteName);

            Assert.Equal(expected, _hostNameProvider.Value);
        }

        [Fact]
        public void Synchronize_UpdatesValue()
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns((string)null);

            Assert.Equal(null, _hostNameProvider.Value);

            // no header present
            HttpRequest request = new DefaultHttpContext().Request;
            _hostNameProvider.Synchronize(request, _logger);
            Assert.Equal(null, _hostNameProvider.Value);

            // empty header value
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, string.Empty);
            _hostNameProvider.Synchronize(request, _logger);
            Assert.Equal(null, _hostNameProvider.Value);

            // host provided via header - expect update
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test.azurewebsites.net");
            _hostNameProvider.Synchronize(request, _logger);
            Assert.Equal("test.azurewebsites.net", _hostNameProvider.Value);
            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, logs.Count);
            Assert.Equal("HostName updated from '(null)' to 'test.azurewebsites.net'", logs[0].FormattedMessage);

            // no change in header value - no update expected
            _loggerProvider.ClearAllLogMessages();
            _hostNameProvider.Synchronize(request, _logger);
            Assert.Equal("test.azurewebsites.net", _hostNameProvider.Value);
            Assert.Equal("test.azurewebsites.net", _hostNameProvider.Value);
            logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(0, logs.Count);

            // another change - expect update
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test2.azurewebsites.net");
            _hostNameProvider.Synchronize(request, _logger);
            Assert.Equal("test2.azurewebsites.net", _hostNameProvider.Value);
            logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, logs.Count);
            Assert.Equal("HostName updated from 'test.azurewebsites.net' to 'test2.azurewebsites.net'", logs[0].FormattedMessage);
        }
    }
}
