// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Rpc;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class LanguageWorkerChannelManagerEndToEndTests : IClassFixture<LanguageWorkerChannelManagerEndToEndTests.TestFixture>
    {
        private IWebHostLanguageWorkerChannelManager _languageWorkerChannelManager;
        public LanguageWorkerChannelManagerEndToEndTests(TestFixture fixture)
        {
            Fixture = fixture;
            _languageWorkerChannelManager = (IWebHostLanguageWorkerChannelManager) fixture.Host.Services.GetService(typeof(IWebHostLanguageWorkerChannelManager));
            
        }

        public TestFixture Fixture { get; set; }

        [Fact]
        public void InitializeAsync_DoNotInitialize_JavaWorker_ProxiesOnly()
        {
            var channelManager = _languageWorkerChannelManager as WebHostLanguageWorkerChannelManager;
            var javaChannel = channelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(javaChannel);
        }

        public class TestFixture : ScriptHostEndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\ProxiesOnly", "proxiesOnly", string.Empty,
                startHost: true)
            {
            }

            protected override Task CreateTestStorageEntities()
            {
                // No need for this.
                return Task.CompletedTask;
            }
        }
    }
}