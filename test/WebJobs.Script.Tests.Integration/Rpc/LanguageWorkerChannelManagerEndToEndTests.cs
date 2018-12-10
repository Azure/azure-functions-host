// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class LanguageWorkerChannelManagerEndToEndTests : IClassFixture<LanguageWorkerChannelManagerEndToEndTests.TestFixture>
    {
        private ILanguageWorkerChannelManager _languageWorkerChannelManager;
        public LanguageWorkerChannelManagerEndToEndTests(TestFixture fixture)
        {
            Fixture = fixture;
            _languageWorkerChannelManager = (ILanguageWorkerChannelManager) fixture.Host.Services.GetService(typeof(ILanguageWorkerChannelManager));
            
        }

        public TestFixture Fixture { get; set; }

        [Fact (Skip = "https://github.com/Azure/azure-functions-host/issues/3872")]
        public void InitializeAsync_DoNotInitialize_JavaWorker()
        {
            var javaChannel = _languageWorkerChannelManager.GetChannel(LanguageWorkerConstants.JavaLanguageWorkerName);
            Assert.Null(javaChannel);
        }

        public class TestFixture : ScriptHostEndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Node", "node", LanguageWorkerConstants.NodeLanguageWorkerName,
                startHost: true, functions: new[] { "HttpTrigger" })
            {
            }
        }
    }
}