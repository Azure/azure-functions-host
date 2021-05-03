using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Storage
{
    public class ExplicitStorageReferenceNotAllowedEndToEndTests : IClassFixture<ExplicitStorageReferenceNotAllowedEndToEndTests.StorageReferenceNotAllowedTestFixture>
    {
        private EndToEndTestFixture _fixture;

        public ExplicitStorageReferenceNotAllowedEndToEndTests(StorageReferenceNotAllowedTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StorageV12TypesNotAllowed()
        {
            // The point of this test is to verify that a user CANNOT #r and use storage v12 types in CSX
            _fixture.AssertScriptHostErrors();

            string testData = Guid.NewGuid().ToString();
            await _fixture.Host.BeginFunctionAsync("StorageReferenceV12", testData);
        }

        public class StorageReferenceNotAllowedTestFixture : EndToEndTestFixture
        {
            public StorageReferenceNotAllowedTestFixture() : base(@"TestScripts\CSharp", "csharp", RpcWorkerConstants.DotNetLanguageWorkerName)
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
                        "StorageReferenceV12"
                    };
                });
            }
        }
    }
}
