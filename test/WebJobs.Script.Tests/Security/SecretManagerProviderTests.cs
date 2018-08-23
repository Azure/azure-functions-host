// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class SecretManagerProviderTests
    {
        private readonly ScriptApplicationHostOptions _options;
        private readonly TestChangeTokenSource _tokenSource;
        private readonly DefaultSecretManagerProvider _provider;

        public SecretManagerProviderTests()
        {
            var mockIdProvider = new Mock<IHostIdProvider>();
            var mockConfiguration = new Mock<IConfiguration>();

            _options = new ScriptApplicationHostOptions
            {
                SecretsPath = "c:\\path1"
            };
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(_options);
            _tokenSource = new TestChangeTokenSource();
            var changeTokens = new[] { _tokenSource };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);

            _provider = new DefaultSecretManagerProvider(optionsMonitor, mockIdProvider.Object, mockConfiguration.Object,
                new TestEnvironment(), NullLoggerFactory.Instance);
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
