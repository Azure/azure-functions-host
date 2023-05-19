using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class CSharpPrecompiledEndToEndTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("dotnet")]
        public async Task InProc_ChoosesCorrectLanguage(string functionWorkerRuntime)
        {
            var fixture = new CSharpPrecompiledEndToEndTestFixture("WebJobsStartupTests", functionWorkerRuntime: functionWorkerRuntime);
            try
            {
                await fixture.InitializeAsync();
                var client = fixture.Host.HttpClient;

                var echo = Guid.NewGuid().ToString();
                var response = await client.GetAsync($"api/echo?echo={echo}");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(echo, await response.Content.ReadAsStringAsync());

                // Previous bug incorrectly chose dotnet-isolated as the Language for C# precompiled functions
                // if FUNCTIONS_WORKER_RUNTIME was missing
                var metadataManager = fixture.Host.WebHostServices.GetService<IFunctionMetadataManager>();
                var metadata = metadataManager.GetFunctionMetadata();
                foreach (var functionMetadata in metadata)
                {
                    Assert.Equal(DotNetScriptTypes.DotNetAssembly, functionMetadata.Language);
                }
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }
    }
}
