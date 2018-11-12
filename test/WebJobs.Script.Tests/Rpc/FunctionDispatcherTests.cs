// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
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
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();
            var eventManager = new Mock<IScriptEventManager>();
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = funcMetadataLanguage
            };
            FunctionDispatcher functionDispatcher = new FunctionDispatcher(eventManager.Object, new TestRpcServer(), workerConfigs, language);
            Assert.True(functionDispatcher.IsSupported(func1));
        }

        [Theory]
        [InlineData("node", "java")]
        [InlineData("java", "node")]
        [InlineData("python", "")]
        public static void IsSupported_Returns_False(string language, string funcMetadataLanguage)
        {
            var workerConfigs = TestHelpers.GetTestWorkerConfigs();
            var eventManager = new Mock<IScriptEventManager>();
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = funcMetadataLanguage
            };
            FunctionDispatcher functionDispatcher = new FunctionDispatcher(eventManager.Object, new TestRpcServer(),  workerConfigs, language);
            Assert.False(functionDispatcher.IsSupported(func1));
        }
    }
}
