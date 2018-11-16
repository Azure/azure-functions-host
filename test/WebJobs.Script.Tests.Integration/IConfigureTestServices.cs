using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration
{
    internal interface IConfigureTestHostBuilder
    {
        void Configure(IHostBuilder builder);
    }
}
