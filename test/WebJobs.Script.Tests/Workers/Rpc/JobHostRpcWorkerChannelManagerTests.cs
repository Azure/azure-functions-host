// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    /// <summary>
    /// Tests to unit test <see cref="JobHostRpcWorkerChannelManager"/>
    /// </summary>
    public class JobHostRpcWorkerChannelManagerTests
    {
        /// <summary>
        /// Logger instance.
        /// </summary>
        private static readonly ILoggerFactory _loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();

        /// <summary>
        /// Mocked Java worker channel.
        /// </summary>
        private Mock<IRpcWorkerChannel> _workerChannelJava1 = new Mock<IRpcWorkerChannel>();

        /// <summary>
        /// Mocked Java2 worker channel.
        /// </summary>
        private Mock<IRpcWorkerChannel> _workerChannelJava2 = new Mock<IRpcWorkerChannel>();

        /// <summary>
        /// Mocked Node worker channel.
        /// </summary>
        private Mock<IRpcWorkerChannel> _workerChannelJs1 = new Mock<IRpcWorkerChannel>();

        /// <summary>
        /// Mocked Node2 worker channel.
        /// </summary>
        private Mock<IRpcWorkerChannel> _workerChannelJs2 = new Mock<IRpcWorkerChannel>();

        /// <summary>
        /// Mocked powershell worker channel.
        /// </summary>
        private Mock<IRpcWorkerChannel> _workerChannelPs1 = new Mock<IRpcWorkerChannel>();

        /// <summary>
        /// Mocked powershell2 worker channel.
        /// </summary>
        private Mock<IRpcWorkerChannel> _workerChannelPs2 = new Mock<IRpcWorkerChannel>();

        /// <summary>
        /// <see cref="JobHostRpcWorkerChannelManager"/> instance.
        /// </summary>
        private JobHostRpcWorkerChannelManager _jobHostRpcWorkerChannelManager = new JobHostRpcWorkerChannelManager(_loggerFactory);

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostRpcWorkerChannelManagerTests"/> class.
        /// </summary>
        public JobHostRpcWorkerChannelManagerTests()
        {
            _workerChannelJava1.Setup(wc => wc.Id).Returns("java1");
            _workerChannelJava2.Setup(wc => wc.Id).Returns("java2");

            _workerChannelJs1.Setup(wc => wc.Id).Returns("js1");
            _workerChannelJs2.Setup(wc => wc.Id).Returns("js2");

            _workerChannelPs1.Setup(wc => wc.Id).Returns("ps1");
            _workerChannelPs2.Setup(wc => wc.Id).Returns("ps2");
        }

        /// <summary>
        /// Tests <see cref="JobHostRpcWorkerChannelManager.AddChannel(IRpcWorkerChannel, string)"/>.
        /// </summary>
        [Fact]
        public void TestAddChannel()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");

            Assert.Equal(3, _jobHostRpcWorkerChannelManager.GetChannels().Count());
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJava1.Object), "Job Manager doesn't contains 'java1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelPs1.Object), "Job Manager doesn't contains 'ps1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJs1.Object), "Job Manager doesn't contains 'js1'");

            Assert.Equal(_workerChannelJava1.Object, _jobHostRpcWorkerChannelManager.GetChannels("java").First());
            Assert.Equal(_workerChannelPs1.Object, _jobHostRpcWorkerChannelManager.GetChannels("powershell").First());
            Assert.Equal(_workerChannelJs1.Object, _jobHostRpcWorkerChannelManager.GetChannels("node").First());
        }

        /// <summary>
        /// Tests <see cref="JobHostRpcWorkerChannelManager.AddChannel(IRpcWorkerChannel, string)"/> Concurrency.
        /// </summary>
        [Fact]
        public void TestAddChannelConcurrency()
        {
            for (int j = 0; j < 100; j++)
            {
                _jobHostRpcWorkerChannelManager.ShutdownChannels();
                List<Thread> startedThreads = new List<Thread>();
                for (int i = 0; i < 100; i++)
                {
                    Thread addChannelThread = new Thread(new ThreadStart(Create100Channels));
                    addChannelThread.Start();
                    startedThreads.Add(addChannelThread);
                }
                startedThreads.ForEach(thread => thread.Join());
                Assert.Equal(10000, _jobHostRpcWorkerChannelManager.GetChannels().Count());
            }
        }

        /// <summary>
        /// Creates and adds 100 random worker channels to <see cref="JobHostRpcWorkerChannelManager"/> instance.
        /// </summary>
        private void Create100Channels()
        {
            for (int i = 0; i < 100; i++)
            {
                Mock<IRpcWorkerChannel> workerChannelJs = new Mock<IRpcWorkerChannel>();
                workerChannelJs.Setup(wc => wc.Id).Returns(Guid.NewGuid().ToString());
                _jobHostRpcWorkerChannelManager.AddChannel(workerChannelJs.Object, "java");
            }
        }

        /// <summary>
        /// Tests <see cref="JobHostRpcWorkerChannelManager.ShutdownChannels()"/>.
        /// </summary>
        [Fact]
        public void TestShutDownChannel()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");
            _jobHostRpcWorkerChannelManager.ShutdownChannels();

            Assert.Equal(0, _jobHostRpcWorkerChannelManager.GetChannels().Count());
        }

        /// <summary>
        /// Tests <see cref="JobHostRpcWorkerChannelManager.ShutdownChannelIfExistsAsync(string, System.Exception)"/>.
        /// </summary>
        [Fact]
        public void TestShutDownChannelIfExists()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava2.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");
            _ = _jobHostRpcWorkerChannelManager.ShutdownChannelIfExistsAsync("java2", null).Result;

            Assert.Equal(3, _jobHostRpcWorkerChannelManager.GetChannels().Count());
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJava1.Object), "Job Manager doesn't contains 'java1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelPs1.Object), "Job Manager doesn't contains 'ps1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJs1.Object), "Job Manager doesn't contains 'js1'");

            Assert.Equal(_workerChannelJava1.Object, _jobHostRpcWorkerChannelManager.GetChannels("java").First());
            Assert.Equal(_workerChannelPs1.Object, _jobHostRpcWorkerChannelManager.GetChannels("powershell").First());
            Assert.Equal(_workerChannelJs1.Object, _jobHostRpcWorkerChannelManager.GetChannels("node").First());
        }

        /// <summary>
        /// Tests <see cref="JobHostRpcWorkerChannelManager.GetChannels()"/>.
        /// </summary>
        [Fact]
        public void TestGetChannels()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs2.Object, "node");

            Assert.Equal(4, _jobHostRpcWorkerChannelManager.GetChannels().Count());
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJava1.Object), "Job Manager doesn't contains 'java1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelPs1.Object), "Job Manager doesn't contains 'ps1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJs1.Object), "Job Manager doesn't contains 'js1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels().Contains(_workerChannelJs2.Object), "Job Manager doesn't contains 'js2'");
        }

        /// <summary>
        /// Tests <see cref="JobHostRpcWorkerChannelManager.GetChannels(string)"/>.
        /// </summary>
        [Fact]
        public void TestGetChannels_WithLanguage()
        {
            _jobHostRpcWorkerChannelManager.ShutdownChannels();
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJava1.Object, "java");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs1.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelPs2.Object, "powershell");
            _jobHostRpcWorkerChannelManager.AddChannel(_workerChannelJs1.Object, "node");

            Assert.Equal(4, _jobHostRpcWorkerChannelManager.GetChannels().Count());
            Assert.Equal(1, _jobHostRpcWorkerChannelManager.GetChannels("java").Count());
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels("java").Contains(_workerChannelJava1.Object), "Job Manager doesn't contains 'java1'");
            Assert.Equal(2, _jobHostRpcWorkerChannelManager.GetChannels("powershell").Count());
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels("powershell").Contains(_workerChannelPs1.Object), "Job Manager doesn't contains 'ps1'");
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels("powershell").Contains(_workerChannelPs2.Object), "Job Manager doesn't contains 'ps2'");
            Assert.Equal(1, _jobHostRpcWorkerChannelManager.GetChannels("node").Count());
            Assert.True(_jobHostRpcWorkerChannelManager.GetChannels("node").Contains(_workerChannelJs1.Object), "Job Manager doesn't contains 'js2'");

            // test case insensitivity
            Assert.Equal(1, _jobHostRpcWorkerChannelManager.GetChannels("Java").Count());
        }
    }
}
