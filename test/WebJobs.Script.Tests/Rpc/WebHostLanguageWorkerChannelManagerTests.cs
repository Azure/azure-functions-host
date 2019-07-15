// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class WebHostLanguageWorkerChannelManagerTests
    {
        private WebHostLanguageWorkerChannelManager _languageWorkerChannelManager;
        private IScriptEventManager _eventManager;
        private IEnvironment _testEnvironment;
        private IRpcServer _rpcServer;
        private TestLoggerProvider _loggerProvider;
        private ILoggerFactory _loggerFactory;
        private LanguageWorkerOptions _languageWorkerOptions;
        private Mock<ILanguageWorkerProcessFactory> _languageWorkerProcessFactory;
        private ILanguageWorkerChannelFactory _languageWorkerChannelFactory;
        private IOptionsMonitor<ScriptApplicationHostOptions> _optionsMonitor;
        private Mock<ILanguageWorkerProcess> _languageWorkerProcess;

        private string _scriptRootPath = @"c:\testing\FUNCTIONS-TEST";
        private IDictionary<string, string> _capabilities = new Dictionary<string, string>()
            {
                { "StandbyModeEnabled", "true" }
            };

        public WebHostLanguageWorkerChannelManagerTests()
        {
            _eventManager = new ScriptEventManager();
            _rpcServer = new TestRpcServer();
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _testEnvironment = new TestEnvironment();
            _loggerFactory.AddProvider(_loggerProvider);
            _languageWorkerProcess = new Mock<ILanguageWorkerProcess>();
            _languageWorkerOptions = new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = @"c:\testing\FUNCTIONS-TEST\test$#"
            };
            _optionsMonitor = TestHelpers.CreateOptionsMonitor(applicationHostOptions);

            _languageWorkerProcessFactory = new Mock<ILanguageWorkerProcessFactory>();
            _languageWorkerProcessFactory.Setup(m => m.CreateLanguageWorkerProcess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(_languageWorkerProcess.Object);

            _languageWorkerChannelFactory = new TestLanguageWorkerChannelFactory(_eventManager, null, _scriptRootPath);
            _languageWorkerChannelManager = new WebHostLanguageWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _languageWorkerChannelFactory, _optionsMonitor);
        }

        [Fact]
        public void CreateChannels_Succeeds()
        {
            string language = LanguageWorkerConstants.JavaLanguageWorkerName;
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(language);
            var initializedChannel = _languageWorkerChannelManager.GetChannel(language);
            ILanguageWorkerChannel javaWorkerChannel2 = CreateTestChannel(LanguageWorkerConstants.JavaLanguageWorkerName);

            Assert.NotNull(initializedChannel);
            Assert.Equal(javaWorkerChannel.Id, initializedChannel.Id);
            Assert.Equal(_languageWorkerChannelManager.GetChannels(LanguageWorkerConstants.JavaLanguageWorkerName).Count(), 2);
        }

        [Fact]
        public void ShutdownStandByChannels_Succeeds()
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.JavaLanguageWorkerName);
            _languageWorkerChannelManager = new WebHostLanguageWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _languageWorkerChannelFactory, _optionsMonitor);

            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(LanguageWorkerConstants.JavaLanguageWorkerName);

            ILanguageWorkerChannel nodeWorkerChannel = CreateTestChannel(LanguageWorkerConstants.NodeLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();
            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            Assert.Null(_languageWorkerChannelManager.GetChannels(LanguageWorkerConstants.NodeLanguageWorkerName));
            initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.NotNull(initializedChannel);
            Assert.Equal(javaWorkerChannel.Id, initializedChannel.Id);
        }

        [Fact]
        public void ShutdownStandByChannels_WorkerRuntinmeDotNet_Succeeds()
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName);
            _languageWorkerChannelManager = new WebHostLanguageWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _languageWorkerChannelFactory, _optionsMonitor);

            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            ILanguageWorkerChannel nodeWorkerChannel = CreateTestChannel(LanguageWorkerConstants.NodeLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();
            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public void ShutdownChannels_Succeeds()
        {
            string javaWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(LanguageWorkerConstants.JavaLanguageWorkerName);

            string nodeWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel nodeWorkerChannel = CreateTestChannel(LanguageWorkerConstants.NodeLanguageWorkerName);

            // Shutdown
            _languageWorkerChannelManager.ShutdownChannels();
            Assert.Null(_languageWorkerChannelManager.GetChannels(LanguageWorkerConstants.JavaLanguageWorkerName));
            Assert.Null(_languageWorkerChannelManager.GetChannels(LanguageWorkerConstants.NodeLanguageWorkerName));

            // Verify disposed
            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public void ShutdownStandyChannels_WorkerRuntime_Node_Set()
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.NodeLanguageWorkerName);
            _languageWorkerChannelManager = new WebHostLanguageWorkerChannelManager(_eventManager, _testEnvironment, _loggerFactory, _languageWorkerChannelFactory, _optionsMonitor);

            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(LanguageWorkerConstants.JavaLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();

            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public void ShutdownStandbyChannels_WorkerRuntime_Not_Set()
        {
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(LanguageWorkerConstants.JavaLanguageWorkerName);

            _languageWorkerChannelManager.ShutdownChannels();

            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public void ShutdownChannelsIfExist_Succeeds()
        {
            ILanguageWorkerChannel javaWorkerChannel1 = CreateTestChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            ILanguageWorkerChannel javaWorkerChannel2 = CreateTestChannel(LanguageWorkerConstants.JavaLanguageWorkerName);

            _languageWorkerChannelManager.ShutdownChannelIfExists(LanguageWorkerConstants.JavaLanguageWorkerName, javaWorkerChannel1.Id);
            _languageWorkerChannelManager.ShutdownChannelIfExists(LanguageWorkerConstants.JavaLanguageWorkerName, javaWorkerChannel2.Id);

            Assert.Empty(_languageWorkerChannelManager.GetChannels(LanguageWorkerConstants.JavaLanguageWorkerName));

            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        private ILanguageWorkerChannel CreateTestChannel(string language)
        {
            var testChannel = _languageWorkerChannelFactory.CreateLanguageWorkerChannel(_scriptRootPath, language, null, 0, null);
            _languageWorkerChannelManager.AddOrUpdateWorkerChannels(language, testChannel);
            return testChannel;
        }
    }
}