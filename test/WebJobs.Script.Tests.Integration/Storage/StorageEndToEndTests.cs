using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
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
            _fixture.AssertNoScriptHostErrors();

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
                        Version = "4.0.4"
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


    public class NoExplicitStorageReferenceEndToEndTests : IClassFixture<NoExplicitStorageReferenceEndToEndTests.NoStorageReferenceTestFixture>
    {
        private EndToEndTestFixture _fixture;

        public NoExplicitStorageReferenceEndToEndTests(NoStorageReferenceTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task CanUseStorageV9Types()
        {
            // The point of this test is to verify back compat behavior that a user can still #r and use storage v9 types in CSX, 
            // even if storage extension is not installed 

            _fixture.AssertNoScriptHostErrors();

            string testData = Guid.NewGuid().ToString();

            await _fixture.Host.BeginFunctionAsync("StorageReferenceV9", testData);

            await TestHelpers.Await(() =>
            {
                // make sure the function executed successfully
                var logs = _fixture.Host.GetScriptHostLogMessages();
                return logs.Any(p => p.FormattedMessage != null && p.FormattedMessage.Contains(testData));
            }, userMessageCallback: _fixture.Host.GetLog);
        }

        [Fact]
        public async Task CanUseStorageV11Types()
        {
            // The point of this test is to verify back compat behavior that a user can still #r and use storage v11 types in CSX, 
            // even if storage extension is not installed 

            _fixture.AssertNoScriptHostErrors();

            string testData = Guid.NewGuid().ToString();

            await _fixture.Host.BeginFunctionAsync("StorageReferenceV11", testData);

            await TestHelpers.Await(() =>
            {
                // make sure the function executed successfully
                var logs = _fixture.Host.GetScriptHostLogMessages();
                return logs.Any(p => p.FormattedMessage != null && p.FormattedMessage.Contains(testData));
            }, userMessageCallback: _fixture.Host.GetLog);
        }

        public class NoStorageReferenceTestFixture : EndToEndTestFixture
        {
            public NoStorageReferenceTestFixture() : base(@"TestScripts\CSharp", "csharp", RpcWorkerConstants.DotNetLanguageWorkerName)
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
                        "StorageReferenceV11"
                    };
                });
            }
        }
    }
}
