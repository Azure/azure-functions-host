using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class FunctionAssemblyLoadContextEndToEndTests : IDisposable
    {
        HostProcessLauncher _launcher;

        [Fact]
        public async Task Fallback_IsThreadSafe()
        {
            _launcher = new HostProcessLauncher("AssemblyLoadContextRace");
            await _launcher.StartHostAsync();

            var client = _launcher.HttpClient;
            var response = await client.GetAsync($"api/Function1");

            // The function does all the validation internally.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public void Dispose()
        {
            _launcher?.Dispose();
        }
    }
}
