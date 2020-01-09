using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Storage
{
    public class StorageEndToEndTests : IClassFixture<StorageEndToEndTests.StorageTestFixture>
    {
        private StorageTestFixture _fixture;

        public StorageEndToEndTests(StorageTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task QueueTriggerToBlobRich()
        {
            var logs = _fixture.Host.GetScriptHostLogMessages();

            string id = Guid.NewGuid().ToString();
            string messageContent = string.Format("{{ \"id\": \"{0}\" }}", id);
            CloudQueueMessage message = new CloudQueueMessage(messageContent);

            await _fixture.TestQueue.AddMessageAsync(message);

            var resultBlob = _fixture.TestOutputContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob);
            Assert.Equal(TestHelpers.RemoveByteOrderMarkAndWhitespace(messageContent), TestHelpers.RemoveByteOrderMarkAndWhitespace(result));
        }

        public class StorageTestFixture : EndToEndTestFixture
        {
            public StorageTestFixture() : base(@"TestScripts\CSharp", "csharp", RpcWorkerConstants.DotNetLanguageWorkerName)
            {
            }

            protected override ExtensionPackageReference[] GetExtensionsToInstall()
            {
                // This test fixture should use the most recent major version of the storage extension, so update this as required.
                return new ExtensionPackageReference[]
                {
                    new ExtensionPackageReference
                    {
                        Id = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version = "3.0.10"
                    }
                };
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "QueueTriggerToBlobRichTypes",
                    };
                });
            }
        }
    }


    public class StorageV9EndToEndTests : IClassFixture<StorageV9EndToEndTests.StorageReferenceTestFixture>
    {
        private EndToEndTestFixture _fixture;

        public StorageV9EndToEndTests(StorageReferenceTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task CanUseStorageV9Types()
        {
            // The point of this test is to verify back compat behavior that a user can still #r and use storage v9 types in CSX, 
            // regardless of whether the storage extension installed (and of what version)

            string testData = Guid.NewGuid().ToString();

            await _fixture.Host.BeginFunctionAsync("StorageReferenceV9", testData);

            await TestHelpers.Await(() =>
            {
                // make sure the function executed successfully
                var logs = _fixture.Host.GetScriptHostLogMessages();
                return logs.Any(p => p.FormattedMessage != null && p.FormattedMessage.Contains(testData));
            }, userMessageCallback: _fixture.Host.GetLog);
        }

        public class StorageReferenceTestFixture : EndToEndTestFixture
        {
            public StorageReferenceTestFixture() : base(@"TestScripts\CSharp", "csharp", RpcWorkerConstants.DotNetLanguageWorkerName)
            {
            }

            protected override ExtensionPackageReference[] GetExtensionsToInstall()
            {
                // It is explicitly intended that this scenario has no package references
                return new ExtensionPackageReference[]
                {
                };
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "StorageReferenceV9",
                    };
                });
            }
        }
    }
}
