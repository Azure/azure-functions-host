// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.AssemblyAnalyzer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.WebHost.AssemblyAnalysis
{
    public class AssemblyAnalysisServiceTests
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly Mock<IOptionsMonitor<StandbyOptions>> _mockStandbyOptions;

        public AssemblyAnalysisServiceTests()
        {
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory(new[] { _loggerProvider });
            _mockEnvironment = new Mock<IEnvironment>();
            _mockStandbyOptions = new Mock<IOptionsMonitor<StandbyOptions>>();
            _mockStandbyOptions.Setup(m => m.CurrentValue).Returns(new StandbyOptions { InStandbyMode = false });
        }

        [Fact]
        public void AnalyzeFunctionAssemblies_UnoptimizedAssembly_LogsDiagnosticEvent()
        {
            // Use the test assembly itself — debug builds are not R2R compiled.
            string dllPath = typeof(AssemblyAnalysisServiceTests).Assembly.Location;
            var service = CreateService(CreateJobHost(dllPath), Path.GetDirectoryName(dllPath));

            service.AnalyzeFunctionAssemblies();

            var logs = _loggerProvider.GetAllLogMessages();
            var diagnosticEvent = logs.SingleOrDefault(l =>
                l.Level == LogLevel.Warning &&
                l.State?.Any(kv => kv.Key == ScriptConstants.ErrorCodeKey &&
                                   (string)kv.Value == DiagnosticEventConstants.FunctionAssemblyNotReadyToRunErrorCode) == true);

            Assert.NotNull(diagnosticEvent);
        }

        [Fact]
        public void AnalyzeFunctionAssemblies_NonExistentPath_LogsDiagnosticEvent()
        {
            // IsReadyToRunOptimized catches file-not-found and returns false (unoptimized).
            const string fakeDllPath = "D:/doesnotexist/nonexistent.dll";
            var service = CreateService(CreateJobHost(fakeDllPath), Path.GetDirectoryName(fakeDllPath));

            service.AnalyzeFunctionAssemblies();

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Contains(logs, l =>
                l.Level == LogLevel.Warning &&
                l.State?.Any(kv => kv.Key == ScriptConstants.ErrorCodeKey &&
                                   (string)kv.Value == DiagnosticEventConstants.FunctionAssemblyNotReadyToRunErrorCode) == true);
        }

        [Fact]
        public void AnalyzeFunctionAssemblies_NoDllFunctions_DoesNotLogDiagnosticEvent()
        {
            // Node/Python functions have .js/.py ScriptFiles — no R2R check should happen.
            var mockJobHost = new Mock<IScriptJobHost>();
            var metadata = new FunctionMetadata { Name = "NodeFunc", ScriptFile = "index.js" };
            var descriptor = new FunctionDescriptor("NodeFunc", null, metadata, null, null, null, null);
            mockJobHost.Setup(h => h.Functions).Returns(new[] { descriptor }.ToImmutableArray());

            var service = CreateService(mockJobHost.Object);

            service.AnalyzeFunctionAssemblies();

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.DoesNotContain(logs, l =>
                l.State?.Any(kv => kv.Key == ScriptConstants.ErrorCodeKey &&
                                   (string)kv.Value == DiagnosticEventConstants.FunctionAssemblyNotReadyToRunErrorCode) == true);
        }

        [Fact]
        public void AnalyzeFunctionAssemblies_DuplicateScriptFiles_ChecksOnce()
        {
            // Two functions pointing to the same DLL should only trigger one diagnostic event
            // because analysis stops at the first unoptimized assembly.
            string dllPath = typeof(AssemblyAnalysisServiceTests).Assembly.Location;
            string scriptRoot = Path.GetDirectoryName(dllPath);
            var mockJobHost = new Mock<IScriptJobHost>();

            var metadata1 = new FunctionMetadata { Name = "Func1", ScriptFile = dllPath };
            var metadata2 = new FunctionMetadata { Name = "Func2", ScriptFile = dllPath };
            var descriptor1 = new FunctionDescriptor("Func1", null, metadata1, null, null, null, null);
            var descriptor2 = new FunctionDescriptor("Func2", null, metadata2, null, null, null, null);
            mockJobHost.Setup(h => h.Functions).Returns(new[] { descriptor1, descriptor2 }.ToImmutableArray());

            var service = CreateService(mockJobHost.Object, scriptRoot);

            service.AnalyzeFunctionAssemblies();

            var logs = _loggerProvider.GetAllLogMessages();
            var diagnosticEvents = logs.Where(l =>
                l.State?.Any(kv => kv.Key == ScriptConstants.ErrorCodeKey &&
                                   (string)kv.Value == DiagnosticEventConstants.FunctionAssemblyNotReadyToRunErrorCode) == true).ToList();

            // Only one diagnostic event, not two.
            Assert.Single(diagnosticEvents);
        }

        [Fact]
        public void AnalyzeFunctionAssemblies_NullJobHost_DoesNotThrow()
        {
            var service = CreateService(jobHost: null);

            // Should return early without throwing.
            service.AnalyzeFunctionAssemblies();

            Assert.Empty(_loggerProvider.GetAllLogMessages().Where(l => l.Level >= LogLevel.Warning));
        }

        private TestableAssemblyAnalysisService CreateService(IScriptJobHost jobHost, string scriptRootPath = null)
        {
            return new TestableAssemblyAnalysisService(
                _mockEnvironment.Object,
                _loggerFactory,
                _mockStandbyOptions.Object,
                jobHost,
                scriptRootPath);
        }

        private static IScriptJobHost CreateJobHost(string dllPath)
        {
            var mockJobHost = new Mock<IScriptJobHost>();
            var metadata = new FunctionMetadata { Name = "IsolatedFunc", ScriptFile = dllPath };
            var descriptor = new FunctionDescriptor("IsolatedFunc", null, metadata, null, null, null, null);
            mockJobHost.Setup(h => h.Functions).Returns(new[] { descriptor }.ToImmutableArray());
            return mockJobHost.Object;
        }

        private class TestableAssemblyAnalysisService : AssemblyAnalysisService
        {
            private readonly IScriptJobHost _jobHost;

            public TestableAssemblyAnalysisService(
                IEnvironment environment,
                ILoggerFactory loggerFactory,
                IOptionsMonitor<StandbyOptions> standbyOptionsMonitor,
                IScriptJobHost jobHost,
                string scriptRootPath = null)
                : base(environment, scriptHost: null, loggerFactory, standbyOptionsMonitor,
                    Options.Create(new ScriptApplicationHostOptions { ScriptPath = scriptRootPath }))
            {
                _jobHost = jobHost;
            }

            protected override IScriptJobHost GetJobHost() => _jobHost;
        }
    }
}
