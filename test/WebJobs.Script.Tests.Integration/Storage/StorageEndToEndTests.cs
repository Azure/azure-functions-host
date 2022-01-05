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

            // TODO: Metadata installation of storage packages does not currently work as expected as the 
            // test host installs the extension by default and this may lead to mismatches.
            //protected override ExtensionPackageReference[] GetExtensionsToInstall()
            //{
            //    // This test fixture should use the most recent major version of the storage extension, so update this as required.
            //    return new ExtensionPackageReference[]
            //    {
            //        new ExtensionPackageReference
            //        {
            //            Id = "Microsoft.Azure.WebJobs.Extensions.Storage",
            //            Version = "4.0.4"
            //        }
            //    };
            //}

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
}
