// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class ForwardingLoggerTests
    {
        private const string CategoryName = "TestCategory";

        private readonly MockScriptHostManager _manager = new();
        private readonly ILogger _webHostLogger;
        private readonly ILoggerFactory _webHostLoggerFactory;
        private readonly FakeLogCollector _webHostLogCollector;

        public ForwardingLoggerTests()
        {
            ServiceCollection services = new();
            services.AddFakeLogging();
            IServiceProvider provider = services.BuildServiceProvider();
            _webHostLoggerFactory = provider.GetRequiredService<ILoggerFactory>();
            _webHostLogger = _webHostLoggerFactory.CreateLogger(CategoryName);
            _webHostLogCollector = provider.GetFakeLogCollector();
        }

        [Fact]
        public void Constructor_WithNullInner_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new ForwardingLogger(CategoryName, null!, Mock.Of<IScriptHostManager>()))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("inner");
        }

        [Fact]
        public void Constructor_WithNullManager_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new ForwardingLogger(CategoryName, Mock.Of<ILogger>(), null!))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("manager");
        }

        [Fact]
        public void Log_WithoutScriptHostServices_UsesFallbackLogger()
        {
            ForwardingLogger forwardingLogger = new(CategoryName, _webHostLogger, _manager);

            forwardingLogger.LogInformation("Web host message");

            VerifyLog(_webHostLogCollector, LogLevel.Information, "Web host message");
        }

        [Fact]
        public void Log_WithScriptHostServices_UsesScriptHostLogger()
        {
            _manager.Services = CreateProvider(out FakeLogCollector collector);

            ForwardingLogger forwardingLogger = new(CategoryName, _webHostLogger, _manager);

            forwardingLogger.LogWarning("Script host message");

            _webHostLogCollector.Count.Should().Be(0);
            VerifyLog(collector, LogLevel.Warning, "Script host message");
        }

        [Fact]
        public void Log_WithChangedScriptHostServices_UpdatesToNewLogger()
        {
            _manager.Services = CreateProvider(out FakeLogCollector collector);
            ForwardingLogger forwardingLogger = new(CategoryName, _webHostLogger, _manager);

            forwardingLogger.LogWarning("Script host message");

            _webHostLogCollector.Count.Should().Be(0);
            VerifyLog(collector, LogLevel.Warning, "Script host message");

            _manager.Services = CreateProvider(out FakeLogCollector newCollector);

            forwardingLogger.LogError("New script host message");

            // should not have changed.
            _webHostLogCollector.Count.Should().Be(0);
            collector.Count.Should().Be(1);
            VerifyLog(newCollector, LogLevel.Error, "New script host message");
        }

        [Fact]
        public void Log_WithChangedScriptHostServices_ReturnsToFallback()
        {
            _manager.Services = CreateProvider(out FakeLogCollector collector);
            ForwardingLogger forwardingLogger = new(CategoryName, _webHostLogger, _manager);

            forwardingLogger.LogWarning("Script host message");

            _webHostLogCollector.Count.Should().Be(0);
            VerifyLog(collector, LogLevel.Warning, "Script host message");

            _manager.Services = null;

            forwardingLogger.LogError("New web host message");

            // should not have changed.
            _webHostLogCollector.Count.Should().Be(1);
            collector.Count.Should().Be(1);
            VerifyLog(_webHostLogCollector, LogLevel.Error, "New web host message");
        }

        [Fact]
        public void LoggerT_Constructor_WithNullFactory_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new ForwardingLogger<TestClass>(null!))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("factory");
        }

        [Fact]
        public void LoggerT_Log_LogsToInner()
        {
            ForwardingLogger<object> logger = new(_webHostLoggerFactory);
            logger.LogCritical("Test message");

            VerifyLog(
                _webHostLogCollector,
                LogLevel.Critical,
                "Test message",
                "object");
        }

        [Fact]
        public void EndToEnd_CreatesForwardingLogger()
        {
            _manager.Services = CreateProvider(out FakeLogCollector collector);
            ServiceCollection services = new();
            services.AddSingleton<IScriptHostManager>(_manager);
            services.AddLogging(b => b.AddForwardingLogger().AddFakeLogging());
            services.AddSingleton<TestClass>();

            IServiceProvider provider = services.BuildServiceProvider();

            TestClass test = provider.GetRequiredService<TestClass>();
            test.Logger.LogInformation("Test message");

            provider.GetFakeLogCollector().Count.Should().Be(0);

            string expectedCategory = typeof(TestClass).FullName.Replace('+', '.');
            VerifyLog(collector, LogLevel.Information, "Test message", expectedCategory);
        }

        private static IServiceProvider CreateProvider(out FakeLogCollector collector)
        {
            ServiceCollection services = new();
            services.AddFakeLogging();
            IServiceProvider serviceProvider = services.BuildServiceProvider();
            collector = serviceProvider.GetFakeLogCollector();
            return serviceProvider;
        }

        private static void VerifyLog(
            FakeLogCollector collector, LogLevel level, string message, string category = CategoryName)
        {
            collector.Count.Should().Be(1);
            FakeLogRecord record = collector.LatestRecord;
            using AssertionScope scope = new("Verifying log record");
            record.Category.Should().Be(category);
            record.Level.Should().Be(level);
            record.Message.Should().Be(message);
        }

        private class MockScriptHostManager : IScriptHostManager
        {
#pragma warning disable CS0067 // The event is never used
            public event EventHandler HostInitializing;

            public event EventHandler<ActiveHostChangedEventArgs> ActiveHostChanged;
#pragma warning restore CS0067 // The event is never used

            public IServiceProvider Services { get; set; }

            public ScriptHostState State => Services is null ? ScriptHostState.Default : ScriptHostState.Running;

            public Exception LastError { get; }

            public void Dispose() { }

            public Task RestartHostAsync(string reason, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }

        private class TestClass([ForwardingLogger] ILogger<TestClass> logger)
        {
            public ILogger<TestClass> Logger => logger;
        }
    }
}
