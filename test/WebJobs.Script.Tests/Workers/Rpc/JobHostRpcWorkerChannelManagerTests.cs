// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class JobHostRpcWorkerChannelManagerTests
    {
        private static readonly ILoggerFactory _loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
        private Mock<IRpcWorkerChannel> _workerChannelJava1 = new Mock<IRpcWorkerChannel>();
        private Mock<IRpcWorkerChannel> _workerChannelJava2 = new Mock<IRpcWorkerChannel>();
        private Mock<IRpcWorkerChannel> _workerChannelJs1 = new Mock<IRpcWorkerChannel>();
        private Mock<IRpcWorkerChannel> _workerChannelJs2 = new Mock<IRpcWorkerChannel>();
        private Mock<IRpcWorkerChannel> _workerChannelPs1 = new Mock<IRpcWorkerChannel>();
        private Mock<IRpcWorkerChannel> _workerChannelPs2 = new Mock<IRpcWorkerChannel>();
        private JobHostRpcWorkerChannelManager _jobHostRpcWorkerChannelManager = new JobHostRpcWorkerChannelManager(_loggerFactory);

        public JobHostRpcWorkerChannelManagerTests()
        {
            _workerChannelJava1.Setup(wc => wc.Id).Returns("java1");
            _workerChannelJava2.Setup(wc => wc.Id).Returns("java2");

            _workerChannelJs1.Setup(wc => wc.Id).Returns("js1");
            _workerChannelJs2.Setup(wc => wc.Id).Returns("js2");

            _workerChannelPs1.Setup(wc => wc.Id).Returns("ps1");
            _workerChannelPs2.Setup(wc => wc.Id).Returns("ps2");
        }

        [Fact]
        public void TestAddChannel()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");

            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels().Count(), 3);
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJava1.Object), "Job Manager doesn't contains 'java1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelPs1.Object), "Job Manager doesn't contains 'ps1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJs1.Object), "Job Manager doesn't contains 'js1'");

            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels("java").First(), _workerChannelJava1.Object);
            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels("powershell").First(), _workerChannelPs1.Object);
            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels("node").First(), _workerChannelJs1.Object);
        }

        [Fact]
        public void TestShutDownChannel()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");
            _jobHostRpcWorkerChannelManager.ShutdownChannels();

            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels().Count(), 0);
        }

        [Fact]
        public void TestShutDownChannelIfExists()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava2.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");
            _ = _jobHostRpcWorkerChannelManager.ShutdownChannelIfExistsAsync("java2", null).Result;

            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels().Count(), 3);
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJava1.Object), "Job Manager doesn't contains 'java1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelPs1.Object), "Job Manager doesn't contains 'ps1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJs1.Object), "Job Manager doesn't contains 'js1'");

            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels("java").First(), _workerChannelJava1.Object);
            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels("powershell").First(), _workerChannelPs1.Object);
            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels("node").First(), _workerChannelJs1.Object);
        }

        [Fact]
        public void TestGetChannels()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs2.Object, "node");

            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels().Count(), 4);
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJava1.Object), "Job Manager doesn't contains 'java1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelPs1.Object), "Job Manager doesn't contains 'ps1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJs1.Object), "Job Manager doesn't contains 'js1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJs2.Object), "Job Manager doesn't contains 'js2'");
        }

        [Fact]
        public void TestGetChannels_WithLanguage()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs2.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");

            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels().Count(), 4);
            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels("java").Count(), 1);
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels("java").Contains(_workerChannelJava1.Object), "Job Manager doesn't contains 'java1'");
            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels("powershell").Count(), 2);
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels("powershell").Contains(_workerChannelPs1.Object), "Job Manager doesn't contains 'ps1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels("powershell").Contains(_workerChannelPs2.Object), "Job Manager doesn't contains 'ps2'");
            Assert.Equal(_jobHostRpcWorkerChannelManager.GetChannels("node").Count(), 1);
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels("node").Contains(_workerChannelJs1.Object), "Job Manager doesn't contains 'js2'");
        }
    }
}
