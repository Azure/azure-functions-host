// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Http
{
    public class HttpFunctionInvocationDispatcherTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestDelay_StartProcess(bool startWorkerProcessResult)
        {
            Mock<IOptions<ScriptJobHostOptions>> mockOptions = new Mock<IOptions<ScriptJobHostOptions>>();
            Mock<IScriptEventManager> mockEventManager = new Mock<IScriptEventManager>();
            Mock<IHttpWorkerChannelFactory> mockFactory = new Mock<IHttpWorkerChannelFactory>();
            Mock<IHttpWorkerChannel> mockChannel = new Mock<IHttpWorkerChannel>();

            mockOptions.Setup(a => a.Value).Returns(new ScriptJobHostOptions());
            if (startWorkerProcessResult)
            {
                mockChannel.Setup(a => a.StartWorkerProcessAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(startWorkerProcessResult));
            }
            else
            {
                mockChannel.Setup(a => a.StartWorkerProcessAsync(It.IsAny<CancellationToken>())).Throws(new Exception("Random exception"));
            }

            mockFactory.Setup(a => a.Create(It.IsAny<string>(), It.IsAny<IMetricsLogger>(), It.IsAny<int>())).Returns(mockChannel.Object);

            HttpFunctionInvocationDispatcher dispatcher = new HttpFunctionInvocationDispatcher(mockOptions.Object, null, null, mockEventManager.Object, NullLoggerFactory.Instance, mockFactory.Object);
            Assert.Equal(dispatcher.State, FunctionInvocationDispatcherState.Default);
            try
            {
                await dispatcher.InitializeHttpWorkerChannelAsync(3);
            }
            catch (Exception)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
            if (startWorkerProcessResult)
            {
                Assert.Equal(dispatcher.State, FunctionInvocationDispatcherState.Initialized);
            }
            else
            {
                Assert.NotEqual(dispatcher.State, FunctionInvocationDispatcherState.Initialized);
            }
        }
    }
}
