// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HostFunctionMetadataProviderTests
    {
        private TestMetricsLogger _testMetricsLogger;
        private ScriptApplicationHostOptions _scriptApplicationHostOptions;

        public HostFunctionMetadataProviderTests()
        {
            _testMetricsLogger = new TestMetricsLogger();
            _scriptApplicationHostOptions = new ScriptApplicationHostOptions();
        }

        [Fact]
        public void ReadFunctionMetadata_Succeeds()
        {
            var testLoggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(testLoggerProvider);
            var logger = loggerFactory.CreateLogger<HostFunctionMetadataProvider>();
            string functionsPath = Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "node");
            _scriptApplicationHostOptions.ScriptPath = functionsPath;
            var optionsMonitor = TestHelpers.CreateOptionsMonitor(_scriptApplicationHostOptions);
            var metadataProvider = new HostFunctionMetadataProvider(optionsMonitor, logger, _testMetricsLogger, SystemEnvironment.Instance);
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();

            Assert.Equal(18, metadataProvider.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, false).Result.Length);
            var traces = testLoggerProvider.GetAllLogMessages();
            Assert.True(AreRequiredMetricsEmitted(_testMetricsLogger));

            // Assert that the logs contain the expected messages
            Assert.Equal(2, traces.Count);
            Assert.Equal("Reading functions metadata (Host)", traces[0].FormattedMessage);
            Assert.Equal("18 functions found (Host)", traces[1].FormattedMessage);
        }

        [Fact]
        public void ReadFunctionMetadata_For_WorkerIndexingFormatApp_Fails()
        {
            string functionsPath = Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "PythonWorkerIndexing");
            _scriptApplicationHostOptions.ScriptPath = functionsPath;
            var optionsMonitor = TestHelpers.CreateOptionsMonitor(_scriptApplicationHostOptions);
            var metadataProvider = new HostFunctionMetadataProvider(optionsMonitor, NullLogger<HostFunctionMetadataProvider>.Instance, _testMetricsLogger, SystemEnvironment.Instance);
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();
            Assert.Equal(0, metadataProvider.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, false).Result.Length);
        }

        [Fact]
        public void ReadFunctionMetadata_With_Retry_Succeeds()
        {
            string functionsPath = Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "noderetry");
            _scriptApplicationHostOptions.ScriptPath = functionsPath;
            var optionsMonitor = TestHelpers.CreateOptionsMonitor(_scriptApplicationHostOptions);
            var metadataProvider = new HostFunctionMetadataProvider(optionsMonitor, NullLogger<HostFunctionMetadataProvider>.Instance, _testMetricsLogger, SystemEnvironment.Instance);
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();
            var functionMetadatas = metadataProvider.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, false).Result;

            Assert.Equal(2, functionMetadatas.Length);

            var functionMetadataWithRetry = functionMetadatas.Where(f => f.Name.Contains("HttpTrigger-RetryFunctionJson", StringComparison.OrdinalIgnoreCase));
            Assert.Single(functionMetadataWithRetry);
            var retry = functionMetadataWithRetry.FirstOrDefault().Retry;
            Assert.NotNull(retry);
            Assert.Equal(RetryStrategy.FixedDelay, retry.Strategy);
            Assert.Equal(4, retry.MaxRetryCount);
            Assert.Equal(TimeSpan.Parse("00:00:03"), retry.DelayInterval);

            var functionMetadata = functionMetadatas.Where(f => !f.Name.Contains("HttpTrigger-RetryFunctionJson", StringComparison.OrdinalIgnoreCase));
            Assert.Single(functionMetadataWithRetry);
            Assert.Null(functionMetadata.FirstOrDefault().Retry);
        }

        private bool AreRequiredMetricsEmitted(TestMetricsLogger metricsLogger)
        {
            bool hasBegun = false;
            bool hasEnded = false;
            foreach (string begin in metricsLogger.EventsBegan)
            {
                if (begin.Contains(MetricEventNames.ReadFunctionMetadata.Substring(0, MetricEventNames.ReadFunctionMetadata.IndexOf('{'))))
                {
                    hasBegun = true;
                    break;
                }
            }
            foreach (string end in metricsLogger.EventsEnded)
            {
                if (end.Contains(MetricEventNames.ReadFunctionMetadata.Substring(0, MetricEventNames.ReadFunctionMetadata.IndexOf('{'))))
                {
                    hasEnded = true;
                    break;
                }
            }
            return hasBegun && hasEnded && (metricsLogger.EventsBegan.Contains(MetricEventNames.ReadFunctionsMetadata)
                && metricsLogger.EventsEnded.Contains(MetricEventNames.ReadFunctionsMetadata));
        }

        [Theory]
        [InlineData("node", "test.js")]
        [InlineData("java", "test.jar")]
        [InlineData("CSharp", "test.cs")]
        [InlineData("CSharp", "test.csx")]
        [InlineData("DotNetAssembly", "test.dll")]
        [InlineData(null, "test.x")]
        public void ParseLanguage_Returns_ExpectedLanguage(string expectedLanguage, string scriptFile)
        {
            var configs = TestHelpers.GetTestWorkerConfigs();
            Assert.Equal(expectedLanguage, HostFunctionMetadataProvider.ParseLanguage(scriptFile, configs, null));
        }

        [Theory]
        [InlineData("dllWorker", "dllWorker")] // when FUNCTIONS_WORKER_RUNTIME is set, use the worker
        [InlineData("DotNetAssembly", null)] // when direct, do not consult worker configs and fallback to in-proc
        public void ParseLanguage_WithDllWorker_Returns_ExpectedLanguage(string expectedLanguage, string functionsWorkerRuntime)
        {
            // The logic when a worker claims "dll" is unique because in-proc also claims dll, so test it separately
            var scriptFile = "test.dll";
            var configs = TestHelpers.GetTestWorkerConfigs(includeDllWorker: true);
            Assert.Equal(expectedLanguage, HostFunctionMetadataProvider.ParseLanguage(scriptFile, configs, functionsWorkerRuntime));
        }

        [Theory]
        [InlineData("test.js")]
        [InlineData("test.jar")]
        [InlineData("test.x")]
        [InlineData("test.py")]
        [InlineData("")]
        [InlineData(null)]
        public void ParseLanguage_HttpWorker_Returns_Null(string scriptFile)
        {
            // for these tests, we just care that the FUNCTIONS_WORKER_RUNTIME isn't null or empty
            Assert.Null(HostFunctionMetadataProvider.ParseLanguage(scriptFile, TestHelpers.GetTestWorkerConfigsNoLanguage(), functionsWorkerRuntime: "any"));
        }

        [Fact]
        public void DeterminePrimaryScriptFile_RelativeSourceFileSpecified()
        {
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\shared\queueTrigger.py", new MockFileData(string.Empty) },
                { @"c:\functions\queueTrigger.py", new MockFileData(string.Empty) },
                { @"c:\functions\helper.py", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);
            string scriptFile = HostFunctionMetadataProvider.DeterminePrimaryScriptFile(@"..\shared\queuetrigger.py", @"c:\functions", fileSystem);
            Assert.Equal(@"c:\shared\queueTrigger.py", scriptFile, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_NoClearPrimary_Returns_Null()
        {
            var functionConfig = new JObject();
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\foo.py", new MockFileData(string.Empty) },
                { @"c:\functions\queueTrigger.py", new MockFileData(string.Empty) },
                { @"c:\functions\helper.py", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            Assert.Null(HostFunctionMetadataProvider.DeterminePrimaryScriptFile(null, @"c:\functions", fileSystem));
        }

        [Fact]
        public void DeterminePrimaryScriptFile_NoFiles_Returns_Null()
        {
            string[] functionFiles = new string[0];

            var fileSystem = new MockFileSystem();
            fileSystem.AddDirectory(@"c:\functions");

            Assert.Null(HostFunctionMetadataProvider.DeterminePrimaryScriptFile(null, @"c:\functions", fileSystem));
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_RunFilePresent()
        {
            var functionConfig = new JObject();
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\Run.csx", new MockFileData(string.Empty) },
                { @"c:\functions\Helper.csx", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = HostFunctionMetadataProvider.DeterminePrimaryScriptFile(null, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\Run.csx", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_InitFilePresent()
        {
            var functionConfig = new JObject();
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\__init__.py", new MockFileData(string.Empty) },
                { @"c:\functions\helloworld.py", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = HostFunctionMetadataProvider.DeterminePrimaryScriptFile(null, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\__init__.py", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_SingleFile()
        {
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\Run.csx", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = HostFunctionMetadataProvider.DeterminePrimaryScriptFile(null, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\Run.csx", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_RunTrumpsIndex()
        {
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\run.js", new MockFileData(string.Empty) },
                { @"c:\functions\index.js", new MockFileData(string.Empty) },
                { @"c:\functions\index.mjs", new MockFileData(string.Empty) },
                { @"c:\functions\index.cjs", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = HostFunctionMetadataProvider.DeterminePrimaryScriptFile(string.Empty, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\run.js", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_IndexJsTrumpsMjsAndCjs()
        {
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\index.js", new MockFileData(string.Empty) },
                { @"c:\functions\index.mjs", new MockFileData(string.Empty) },
                { @"c:\functions\index.cjs", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = HostFunctionMetadataProvider.DeterminePrimaryScriptFile(string.Empty, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\index.js", scriptFile);
        }

        [Theory]
        [InlineData("index.js", @"c:\functions\index.js")]
        [InlineData("__init__.py", @"c:\functions\__init__.py")]
        public void DeterminePrimaryScriptFile_MultipleFiles_DefaultFilePresent(string scriptFileProperty, string expectedScriptFilePath)
        {
            var files = new Dictionary<string, MockFileData>
            {
                { expectedScriptFilePath, new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = HostFunctionMetadataProvider.DeterminePrimaryScriptFile(scriptFileProperty, @"c:\functions", fileSystem);
            Assert.Equal(expectedScriptFilePath, scriptFile);
        }

        [Theory]
        [InlineData("run.py", @"c:\functions\run.py")]
        [InlineData("queueTrigger.py", @"c:\functions\queueTrigger.py")]
        [InlineData("helper.py", @"c:\functions\helper.py")]
        [InlineData("test.txt", @"c:\functions\test.txt")]
        public void DeterminePrimaryScriptFile_MultipleFiles_ConfigTrumpsConvention(string scriptFileProperty, string expectedScriptFilePath)
        {
            var files = new Dictionary<string, MockFileData>
            {
                { expectedScriptFilePath, new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string actualScriptFilePath = HostFunctionMetadataProvider.DeterminePrimaryScriptFile(scriptFileProperty, @"c:\functions", fileSystem);
            Assert.Equal(expectedScriptFilePath, actualScriptFilePath);
        }

        [Theory]
        [InlineData("QUEUETriggER.py")]
        [InlineData("queueTrigger.py")]
        public void DeterminePrimaryScriptFile_MultipleFiles_SourceFileSpecified(string scriptFileName)
        {
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\queueTrigger.py", new MockFileData(string.Empty) },
                { @"c:\functions\helper.py", new MockFileData(string.Empty) },
                { @"c:\functions\__init__.py", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);
            string scriptFile = HostFunctionMetadataProvider.DeterminePrimaryScriptFile(scriptFileName, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\queueTrigger.py", scriptFile, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("app.dll", "dotnet", DotNetScriptTypes.DotNetAssembly)]
        [InlineData("app.dll", null, DotNetScriptTypes.DotNetAssembly)]
        [InlineData("app.dll", "any", "dllWorker")]
        public void ParseFunctionMetadata_ResolvesCorrectDotNetLanguage(string scriptFile, string functionsWorkerRuntime, string expectedLanguage)
        {
            var functionJson = new
            {
                scriptFile,
                bindings = new[] { new { } }
            };
            var json = JObject.FromObject(functionJson);
            var scriptRoot = Path.Combine(Path.GetTempPath(), Path.GetTempFileName());

            var fullFileSystem = new FileSystem();
            var fileSystemMock = new Mock<IFileSystem>();
            var fileBaseMock = new Mock<FileBase>();
            fileSystemMock.Setup(f => f.Path).Returns(fullFileSystem.Path);
            fileSystemMock.Setup(f => f.File).Returns(fileBaseMock.Object);
            fileBaseMock.Setup(f => f.Exists(It.IsAny<string>())).Returns(true);

            IList<RpcWorkerConfig> workerConfigs = new List<RpcWorkerConfig>();
            // dotnet skips parsing the worker configs so that in-proc (DotNetAssembly) is chosen
            if (functionsWorkerRuntime != "dotnet")
            {
                workerConfigs = TestHelpers.GetTestWorkerConfigs(includeDllWorker: true);
            }

            var metadata = HostFunctionMetadataProvider.ParseFunctionMetadata("Function1", json, scriptRoot, fileSystemMock.Object, workerConfigs, functionsWorkerRuntime);
            Assert.Equal(expectedLanguage, metadata.Language);
        }
    }
}
