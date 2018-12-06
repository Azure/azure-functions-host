// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
    public class LanguageWorkerChannelManagerTests
    {
        private LanguageWorkerChannelManager _languageWorkerChannelManager;
        private IScriptEventManager _eventManager;
        private IEnvironment _testEnvironment;
        private IRpcServer _rpcServer;
        private TestLoggerProvider _loggerProvider;
        private ILoggerFactory _loggerFactory;
        private LanguageWorkerOptions _languageWorkerOptions;
        private IOptionsMonitor<ScriptApplicationHostOptions> _optionsMonitor;

        private string _scriptRootPath = @"c:\testing\FUNCTIONS-TEST";
        private IDictionary<string, string> _capabilities = new Dictionary<string, string>()
            {
                { "StandbyModeEnabled", "true" }
            };

        public LanguageWorkerChannelManagerTests()
        {
            _eventManager = new ScriptEventManager();
            _rpcServer = new TestRpcServer();
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _testEnvironment = new TestEnvironment();
            _loggerFactory.AddProvider(_loggerProvider);
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
        }

        [Fact]
        public void CreateChannel_Succeeds()
        {
            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor);
            string workerId = Guid.NewGuid().ToString();
            string language = LanguageWorkerConstants.JavaLanguageWorkerName;

            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(workerId, language);
            var initializedChannel = _languageWorkerChannelManager.GetChannel(language);
            Assert.NotNull(initializedChannel);
            Assert.Equal(workerId, initializedChannel.Id);
        }

        [Fact]
        public void ShutdownStandByChannels_Succeeds()
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.JavaLanguageWorkerName);
            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor);

            string javaWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(javaWorkerId, LanguageWorkerConstants.JavaLanguageWorkerName);

            string nodeWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel nodeWorkerChannel = CreateTestChannel(nodeWorkerId, LanguageWorkerConstants.NodeLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();
            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.NotNull(initializedChannel);
            Assert.Equal(javaWorkerId, initializedChannel.Id);
        }

        [Fact]
        public void ShutdownStandByChannels_WorkerRuntinmeDotNet_Succeeds()
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName);
            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor);

            string javaWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(javaWorkerId, LanguageWorkerConstants.JavaLanguageWorkerName);

            string nodeWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel nodeWorkerChannel = CreateTestChannel(nodeWorkerId, LanguageWorkerConstants.NodeLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();
            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public async void ShutdownChannels_Succeeds()
        {
            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor);

            string javaWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(javaWorkerId, LanguageWorkerConstants.JavaLanguageWorkerName);

            string nodeWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel nodeWorkerChannel = CreateTestChannel(nodeWorkerId, LanguageWorkerConstants.NodeLanguageWorkerName);

            // Shutdown
            await _languageWorkerChannelManager.ShutdownChannelsAsync();

            //Verify disposed
            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        private ILanguageWorkerChannel CreateTestChannel(string workerId, string language)
        {
            var testChannel = _languageWorkerChannelManager.CreateLanguageWorkerChannel(workerId, _scriptRootPath, language, null, null, 0);
            // Generate event to mock language worker response
            RpcChannelReadyEvent javarReadyEvent = new RpcChannelReadyEvent(workerId, language, testChannel, "testVersion", _capabilities);
            _eventManager.Publish(javarReadyEvent);
            return testChannel;
        }
    }
}
