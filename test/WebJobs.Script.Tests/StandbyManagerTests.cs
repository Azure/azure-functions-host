// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyManagerTests
    {
        private Mock<IScriptHostManager> _mockHostManager;
        private Mock<IConfigurationRoot> _mockConfiguration;
        private Mock<IOptionsMonitor<ScriptApplicationHostOptions>> _mockOptionsMonitor;
        private Mock<IScriptWebHostEnvironment> _mockWebHostEnvironment;
        private Mock<ILanguageWorkerChannelManager> _mockLanguageWorkerChannelManager;
        private TestEnvironment _testEnvironment;
        private string _testSettingName = "TestSetting";
        private string _testSettingValue = "TestSettingValue";

        public StandbyManagerTests()
        {
            _mockHostManager = new Mock<IScriptHostManager>();
            _mockHostManager.Setup(m => m.State).Returns(ScriptHostState.Running);
            _mockConfiguration = new Mock<IConfigurationRoot>();
            _mockOptionsMonitor = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            _mockWebHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            _mockLanguageWorkerChannelManager = new Mock<ILanguageWorkerChannelManager>();
            _testEnvironment = new TestEnvironment();
        }

        [Fact]
        public async Task Specialize_ResetsConfiguration()
        {
            var manager = new StandbyManager(_mockHostManager.Object, _mockLanguageWorkerChannelManager.Object, _mockConfiguration.Object, _mockWebHostEnvironment.Object, _testEnvironment, _mockOptionsMonitor.Object, NullLogger<StandbyManager>.Instance);

            await manager.SpecializeHostAsync();

            _mockConfiguration.Verify(c => c.Reload());
        }

        [Fact]
        public async Task Specialize_ReloadsEnvironmentVariables()
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.JavaLanguageWorkerName);
            _mockLanguageWorkerChannelManager.Setup(m => m.SpecializeAsync()).Returns(async () =>
            {
                _testEnvironment.SetEnvironmentVariable(_testSettingName, _testSettingValue);
                await Task.Yield();
            });
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.JavaLanguageWorkerName);
            var manager = new StandbyManager(_mockHostManager.Object, _mockLanguageWorkerChannelManager.Object, _mockConfiguration.Object, _mockWebHostEnvironment.Object, _testEnvironment, _mockOptionsMonitor.Object, NullLogger<StandbyManager>.Instance);
            await manager.SpecializeHostAsync();
            Assert.Equal(_testSettingValue, _testEnvironment.GetEnvironmentVariable(_testSettingName));
        }
    }
}
