// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SystemLoggerProviderTests
    {
        private readonly IOptions<ScriptJobHostOptions> _options;
        private readonly IEnvironment _environment = new TestEnvironment();
        private readonly SystemLoggerProvider _provider;
        private bool _inDiagnosticMode;

        public SystemLoggerProviderTests()
        {
            var scriptOptions = new ScriptJobHostOptions
            {
                RootLogPath = Path.GetTempPath()
            };

            _options = new OptionsWrapper<ScriptJobHostOptions>(scriptOptions);

            var debugStateProvider = new Mock<IDebugStateProvider>(MockBehavior.Strict);
            debugStateProvider.Setup(p => p.InDiagnosticMode).Returns(() => _inDiagnosticMode);

            _provider = new SystemLoggerProvider(_options, null, _environment, debugStateProvider.Object, null);
            _provider.SetScopeProvider(new LoggerExternalScopeProvider());
        }

        [Fact]
        public void CreateLogger_ReturnsSystemLogger_ForSystemCategories()
        {
            Assert.IsType<SystemLogger>(_provider.CreateLogger(LogCategories.CreateFunctionCategory("TestFunction")));
            Assert.IsType<SystemLogger>(_provider.CreateLogger(ScriptConstants.LogCategoryHostGeneral));
            Assert.IsType<SystemLogger>(_provider.CreateLogger(LanguageWorkerConstants.CreateLanguageWorkerChannelLogCategory("dotnet", "123")));
            Assert.IsType<SystemLogger>(_provider.CreateLogger(LanguageWorkerConstants.CreateLanguageWorkerProcessLogCategory("dotnet", "123")));
            Assert.IsType<SystemLogger>(_provider.CreateLogger(typeof(ScriptHost).FullName));
        }

        [Fact]
        public void CreateLogger_ReturnsNullLogger_ForNonSystemCategories()
        {
            Assert.IsType<NullLogger>(_provider.CreateLogger(LogCategories.CreateFunctionUserCategory("TestFunction")));
            Assert.IsType<NullLogger>(_provider.CreateLogger(LogCategories.CreateFunctionUserCategory("TestFunction") + ".SomeExtra"));
            Assert.IsType<NullLogger>(_provider.CreateLogger("Worker.LanguageWorker.Abc.123"));
            Assert.IsType<NullLogger>(_provider.CreateLogger("FunctionApp1.Mine"));
            Assert.IsType<NullLogger>(_provider.CreateLogger("Host.General.Other"));
        }

        [Fact]
        public void CreateLogger_DefaultsLogLevelToDebug()
        {
            var logger = _provider.CreateLogger(LogCategories.Startup);
            Assert.True(logger.IsEnabled(LogLevel.Information));
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            Assert.True(logger.IsEnabled(LogLevel.Error));
            Assert.True(logger.IsEnabled(LogLevel.Critical));
            Assert.True(logger.IsEnabled(LogLevel.Debug));
            Assert.False(logger.IsEnabled(LogLevel.Trace));
        }

        [Fact]
        public void CreateLogger_DiagnosticMode_LogsEverything()
        {
            var logger = _provider.CreateLogger(LogCategories.Startup);
            Assert.True(logger.IsEnabled(LogLevel.Information));
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            Assert.True(logger.IsEnabled(LogLevel.Error));
            Assert.True(logger.IsEnabled(LogLevel.Critical));
            Assert.True(logger.IsEnabled(LogLevel.Debug));
            Assert.False(logger.IsEnabled(LogLevel.Trace));

            _inDiagnosticMode = true;
            logger = _provider.CreateLogger(LogCategories.Startup);
            Assert.True(logger.IsEnabled(LogLevel.Information));
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            Assert.True(logger.IsEnabled(LogLevel.Error));
            Assert.True(logger.IsEnabled(LogLevel.Critical));
            Assert.True(logger.IsEnabled(LogLevel.Debug));
            Assert.True(logger.IsEnabled(LogLevel.Trace));
        }
    }
}