// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
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
        public void CreateChannels_Succeeds()
        {
            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor, null);
            string workerId = Guid.NewGuid().ToString();
            string language = LanguageWorkerConstants.JavaLanguageWorkerName;

            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(workerId, language);
            var initializedChannel = _languageWorkerChannelManager.GetChannel(language);
            string javaWorkerId2 = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel2 = CreateTestChannel(javaWorkerId2, LanguageWorkerConstants.JavaLanguageWorkerName);

            Assert.NotNull(initializedChannel);
            Assert.Equal(workerId, initializedChannel.Id);
            Assert.Equal(_languageWorkerChannelManager.GetChannels(LanguageWorkerConstants.JavaLanguageWorkerName).Count(), 2);
        }

        [Fact]
        public void ShutdownStandByChannels_Succeeds()
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.JavaLanguageWorkerName);
            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor, null);

            string javaWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(javaWorkerId, LanguageWorkerConstants.JavaLanguageWorkerName);

            string nodeWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel nodeWorkerChannel = CreateTestChannel(nodeWorkerId, LanguageWorkerConstants.NodeLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();
            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.NodeLanguageWorkerName);
            Assert.Null(initializedChannel);
            Assert.Null(_languageWorkerChannelManager.GetChannels(LanguageWorkerConstants.NodeLanguageWorkerName));
            initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.NotNull(initializedChannel);
            Assert.Equal(javaWorkerId, initializedChannel.Id);
        }

        [Fact]
        public void ShutdownStandByChannels_WorkerRuntinmeDotNet_Succeeds()
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName);
            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor, null);

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
        public void ShutdownChannels_Succeeds()
        {
            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor, null);

            string javaWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(javaWorkerId, LanguageWorkerConstants.JavaLanguageWorkerName);

            string nodeWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel nodeWorkerChannel = CreateTestChannel(nodeWorkerId, LanguageWorkerConstants.NodeLanguageWorkerName);

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
            _testEnvironment = new TestEnvironment();
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.NodeLanguageWorkerName);

            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor, null);
            string javaWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(javaWorkerId, LanguageWorkerConstants.JavaLanguageWorkerName);

            _languageWorkerChannelManager.ScheduleShutdownStandbyChannels();

            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public void ShutdownStandbyChannels_WorkerRuntime_Not_Set()
        {
            _testEnvironment = new TestEnvironment();

            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor, null);
            string javaWorkerId = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel = CreateTestChannel(javaWorkerId, LanguageWorkerConstants.JavaLanguageWorkerName);

            _languageWorkerChannelManager.ShutdownChannels();

            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        [Fact]
        public void ShutdownChannelsIfExist_Succeeds()
        {
            _testEnvironment = new TestEnvironment();
            _languageWorkerChannelManager = new LanguageWorkerChannelManager(_eventManager, _testEnvironment, _rpcServer, _loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(_languageWorkerOptions), _optionsMonitor, null);
            string javaWorkerId1 = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel1 = CreateTestChannel(javaWorkerId1, LanguageWorkerConstants.JavaLanguageWorkerName);

            string javaWorkerId2 = Guid.NewGuid().ToString();
            ILanguageWorkerChannel javaWorkerChannel2 = CreateTestChannel(javaWorkerId2, LanguageWorkerConstants.JavaLanguageWorkerName);

            _languageWorkerChannelManager.ShutdownChannelIfExists(LanguageWorkerConstants.JavaLanguageWorkerName, javaWorkerId1);
            _languageWorkerChannelManager.ShutdownChannelIfExists(LanguageWorkerConstants.JavaLanguageWorkerName, javaWorkerId2);

            Assert.Empty(_languageWorkerChannelManager.GetChannels(LanguageWorkerConstants.JavaLanguageWorkerName));

            var initializedChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(initializedChannel);
        }

        private ILanguageWorkerChannel CreateTestChannel(string workerId, string language)
        {
            var testChannel = _languageWorkerChannelManager.CreateLanguageWorkerChannel(workerId, _scriptRootPath, language, null, 0, false, null);
            // Generate event to mock language worker response
            RpcWebHostChannelReadyEvent javaReadyEvent = new RpcWebHostChannelReadyEvent(workerId, language, testChannel, "testVersion", _capabilities);
            _eventManager.Publish(javaReadyEvent);
            return testChannel;
        }
    }
}
