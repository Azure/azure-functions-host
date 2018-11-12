// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class FunctionDispatcherTests
    {
        [Theory]
        [InlineData("node", "node")]
        [InlineData("java", "java")]
        [InlineData("", "node")]
        [InlineData(null, "java")]
        public static void IsSupported_Returns_True(string language, string funcMetadataLanguage)
        {
            IFunctionDispatcher functionDispatcher = GetTestFunctionDispatcher();
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = funcMetadataLanguage
            };
            Assert.True(functionDispatcher.IsSupported(func1, language));
        }

        [Theory]
        [InlineData("node", "java")]
        [InlineData("java", "node")]
        [InlineData("python", "")]
        public static void IsSupported_Returns_False(string language, string funcMetadataLanguage)
        {
            IFunctionDispatcher functionDispatcher = GetTestFunctionDispatcher();
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = funcMetadataLanguage
            };
            Assert.False(functionDispatcher.IsSupported(func1, language));
        }

        private static IFunctionDispatcher GetTestFunctionDispatcher()
        {
            var eventManager = new Mock<IScriptEventManager>();
            var metricsLogger = new Mock<IMetricsLogger>();
            var languageWorkerChannelManager = new Mock<ILanguageWorkerChannelManager>();
            var loggerFactory = MockNullLogerFactory.CreateLoggerFactory();
            var options = new ScriptJobHostOptions
            {
                RootLogPath = Path.GetTempPath()
            };

            IOptions<ScriptJobHostOptions> scriptOptions = new OptionsManager<ScriptJobHostOptions>(new TestOptionsFactory<ScriptJobHostOptions>(options));

            var workerConfigOptions = new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
            return new FunctionDispatcher(scriptOptions, metricsLogger.Object, eventManager.Object, loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(workerConfigOptions), languageWorkerChannelManager.Object);
        }
    }
}
