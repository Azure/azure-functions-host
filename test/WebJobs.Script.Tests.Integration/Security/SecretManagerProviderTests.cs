﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class SecretManagerProviderTests
    {
        private readonly ScriptApplicationHostOptions _options;
        private readonly TestChangeTokenSource<ScriptApplicationHostOptions> _tokenSource;
        private readonly DefaultSecretManagerProvider _provider;

        public SecretManagerProviderTests()
        {
            var mockIdProvider = new Mock<IHostIdProvider>();

            _options = new ScriptApplicationHostOptions
            {
                SecretsPath = "c:\\path1"
            };
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(_options);
            _tokenSource = new TestChangeTokenSource<ScriptApplicationHostOptions>();
            var changeTokens = new[] { _tokenSource };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);

            var config = TestHelpers.GetTestConfiguration();

            mockIdProvider.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("testhostid");

            IEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "test.azurewebsites.net");
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var hostNameProvider = new HostNameProvider(environment);

            _provider = new DefaultSecretManagerProvider(optionsMonitor, mockIdProvider.Object, config,
                new TestEnvironment(), NullLoggerFactory.Instance, new TestMetricsLogger(), hostNameProvider, new StartupContextProvider(environment, loggerFactory.CreateLogger<StartupContextProvider>()));
        }

        [Fact]
        public void OptionsMonitor_OnChange_ResetsCurrent()
        {
            var manager1 = _provider.Current;
            var manager2 = _provider.Current;
            _tokenSource.SignalChange();
            var manager3 = _provider.Current;

            Assert.Same(manager1, manager2);
            Assert.NotSame(manager1, manager3);
        }
    }
}
