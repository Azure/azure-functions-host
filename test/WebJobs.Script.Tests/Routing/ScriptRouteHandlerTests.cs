// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Moq;
using Xunit;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Tests.Routing
{
    public class ScriptRouteHandlerTests
    {
        [Fact]
        public void WarmupRoute_Is_Idempotent()
        {
            var scriptJobHostMock = new Mock<IScriptJobHost>(MockBehavior.Strict);
            var testEnvironment = new TestEnvironment();
            var testScriptRouteHandler = new ScriptRouteHandler(MockNullLoggerFactory.CreateLoggerFactory(), scriptJobHostMock.Object, testEnvironment, false, true);

            var funcName = "warmup";
            var functions = new Collection<FunctionDescriptor>();
            var function = new FunctionDescriptor(funcName, new TestInvoker(TestInvoke), new FunctionMetadata(), new Collection<ParameterDescriptor>(), null, null, null);
            functions.Add(function);
            scriptJobHostMock.SetupGet(host => host.Functions).Returns(functions);

            var httpContext_1 = new DefaultHttpContext();
            var httpContext_2 = new DefaultHttpContext();

            // make function execution go to a function that waits a long time?
            var warmupCallTask_1 = testScriptRouteHandler.InvokeAsync(httpContext_1, funcName);
            var warmupCallTask_2 = testScriptRouteHandler.InvokeAsync(httpContext_2, funcName);

            Assert.Equal(warmupCallTask_1, warmupCallTask_2);
            Assert.Equal(warmupCallTask_1.Id, warmupCallTask_2.Id);
            Assert.NotEqual(Task.CompletedTask, warmupCallTask_1);
            Assert.NotEqual(Task.CompletedTask, warmupCallTask_2);
        }

        private void TestInvoke(object[] args)
        {
            Task.Delay(1000000);
        }
    }
}
