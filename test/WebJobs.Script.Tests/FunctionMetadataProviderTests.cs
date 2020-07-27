// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionMetadataProviderTests
    {
        private TestMetricsLogger _testMetricsLogger;
        private ScriptApplicationHostOptions _scriptApplicationHostOptions;
        private LanguageWorkerOptions _languageWorkerOptions;

        public FunctionMetadataProviderTests()
        {
            _testMetricsLogger = new TestMetricsLogger();
            _scriptApplicationHostOptions = new ScriptApplicationHostOptions();
            _languageWorkerOptions = new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
        }

        [Fact]
        public void ReadFunctionMetadata_Succeeds()
        {
            string functionsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample\node");
            _scriptApplicationHostOptions.ScriptPath = functionsPath;
            var optionsMonitor = TestHelpers.CreateOptionsMonitor(_scriptApplicationHostOptions);
            var metadataProvider = new FunctionMetadataProvider(optionsMonitor, NullLogger<FunctionMetadataProvider>.Instance, _testMetricsLogger);
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();

            Assert.Equal(18, metadataProvider.GetFunctionMetadata(workerConfigs, false).Length);
            Assert.True(AreRequiredMetricsEmitted(_testMetricsLogger));
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
        [InlineData("")]
        [InlineData("host")]
        [InlineData("Host")]
        [InlineData("-function")]
        [InlineData("_function")]
        [InlineData("function test")]
        [InlineData("function.test")]
        [InlineData("function0.1")]
        public void ValidateFunctionName_ThrowsOnInvalidName(string functionName)
        {
            string functionsPath = "c:\testdir";
            _scriptApplicationHostOptions.ScriptPath = functionsPath;
            var optionsMonitor = TestHelpers.CreateOptionsMonitor(_scriptApplicationHostOptions);
            var metadataProvider = new FunctionMetadataProvider(optionsMonitor, NullLogger<FunctionMetadataProvider>.Instance, _testMetricsLogger);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                metadataProvider.ValidateName(functionName);
            });

            Assert.Equal(string.Format("'{0}' is not a valid function name.", functionName), ex.Message);
        }

        [Theory]
        [InlineData("testwithhost")]
        [InlineData("hosts")]
        [InlineData("myfunction")]
        [InlineData("myfunction-test")]
        [InlineData("myfunction_test")]
        public void ValidateFunctionName_DoesNotThrowOnValidName(string functionName)
        {
            string functionsPath = "c:\testdir";
            _scriptApplicationHostOptions.ScriptPath = functionsPath;
            var optionsMonitor = TestHelpers.CreateOptionsMonitor(_scriptApplicationHostOptions);
            var metadataProvider = new FunctionMetadataProvider(optionsMonitor, NullLogger<FunctionMetadataProvider>.Instance, _testMetricsLogger);

            try
            {
                metadataProvider.ValidateName(functionName);
            }
            catch (InvalidOperationException)
            {
                Assert.True(false, $"Valid function name {functionName} failed validation.");
            }
        }

        [Theory]
        [InlineData("node", "test.js", false)]
        [InlineData("java", "test.jar", false)]
        [InlineData("CSharp", "test.cs", false)]
        [InlineData("CSharp", "test.csx", false)]
        [InlineData("dllWorker", "test.dll", true)] // The test "dllWorker" will claim ".dll" extensions before falling back to DotNetAssembly
        [InlineData("DotNetAssembly", "test.dll", false)]
        [InlineData(null, "test.x", false)]
        public void ParseLanguage_Returns_ExpectedLanguage(string language, string scriptFile, bool includeDllWorker)
        {
            Assert.Equal(language, FunctionMetadataProvider.ParseLanguage(scriptFile, TestHelpers.GetTestWorkerConfigs(includeDllWorker: includeDllWorker)));
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
            Assert.Null(FunctionMetadataProvider.ParseLanguage(scriptFile, TestHelpers.GetTestWorkerConfigsNoLanguage()));
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
            string scriptFile = FunctionMetadataProvider.DeterminePrimaryScriptFile(@"..\shared\queuetrigger.py", @"c:\functions", fileSystem);
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

            Assert.Null(FunctionMetadataProvider.DeterminePrimaryScriptFile(null, @"c:\functions", fileSystem));
        }

        [Fact]
        public void DeterminePrimaryScriptFile_NoFiles_Returns_Null()
        {
            string[] functionFiles = new string[0];

            var fileSystem = new MockFileSystem();
            fileSystem.AddDirectory(@"c:\functions");

            Assert.Null(FunctionMetadataProvider.DeterminePrimaryScriptFile(null, @"c:\functions", fileSystem));
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

            string scriptFile = FunctionMetadataProvider.DeterminePrimaryScriptFile(null, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\Run.csx", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_SingleFile()
        {
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\Run.csx", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = FunctionMetadataProvider.DeterminePrimaryScriptFile(null, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\Run.csx", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_RunTrumpsIndex()
        {
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\run.js", new MockFileData(string.Empty) },
                { @"c:\functions\index.js", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = FunctionMetadataProvider.DeterminePrimaryScriptFile(string.Empty, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\run.js", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_IndexFilePresent()
        {
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\index.js", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = FunctionMetadataProvider.DeterminePrimaryScriptFile("index.js", @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\index.js", scriptFile);
        }

        [Theory]
        [InlineData("run.py", @"c:\functions\run.py")]
        [InlineData("queueTrigger.py", @"c:\functions\queueTrigger.py")]
        [InlineData("helper.py", @"c:\functions\helper.py")]
        [InlineData("test.txt", @"c:\functions\test.txt")]
        public void DeterminePrimaryScriptFile_MultipleFiles_ConfigTrumpsConvention(string scriptFileProperty, string expedtedScriptFilePath)
        {
            var files = new Dictionary<string, MockFileData>
            {
                { expedtedScriptFilePath, new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string actualScriptFilePath = FunctionMetadataProvider.DeterminePrimaryScriptFile(scriptFileProperty, @"c:\functions", fileSystem);
            Assert.Equal(expedtedScriptFilePath, actualScriptFilePath);
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
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);
            string scriptFile = FunctionMetadataProvider.DeterminePrimaryScriptFile(scriptFileName, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\queueTrigger.py", scriptFile, StringComparer.OrdinalIgnoreCase);
        }
    }
}
