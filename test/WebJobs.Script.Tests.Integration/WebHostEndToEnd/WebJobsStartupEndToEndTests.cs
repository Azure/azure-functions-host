using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class WebJobsStartupEndToEndTests
    {
        private const string _projectName = "WebJobsStartupTests";
        private readonly IDictionary<string, string> _envVars;

        public WebJobsStartupEndToEndTests()
        {
            _envVars = new Dictionary<string, string>
            {
                { "WEBSITE_SKU", "Dynamic" }, // only runs in Consumption
                { "MyOptions__MyKey", "WillBeOverwrittenInAppStartup" },
                { "MyOptions__MyOtherKey", "FromEnvironment" },
                { "Cron", "0 0 0 1 1 0" }
            };
        }

        [Fact]
        public async Task ExternalStartup_Succeeds()
        {
            // We need different fixture setup for each test.
            var fixture = new CSharpPrecompiledEndToEndTestFixture(_projectName, _envVars);
            try
            {
                await fixture.InitializeAsync();
                var client = fixture.Host.HttpClient;

                var response = await client.GetAsync($"api/Function1");

                // The function does all the validation internally.
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }

        [Fact]
        public async Task ExternalStartup_InvalidOverwrite_StopsHost()
        {
            _envVars["Cron"] = "* * * * * *";
            _envVars[EnvironmentSettingNames.FunctionsExtensionVersion] = "~4";

            // We need different fixture setup for each test.
            var fixture = new CSharpPrecompiledEndToEndTestFixture(_projectName, _envVars); // Startup.cs will change this.
            try
            {
                await fixture.InitializeAsync();
                var client = fixture.Host.HttpClient;

                var response = await client.GetAsync($"api/Function1");

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

                var manager = fixture.Host.WebHostServices.GetService<IScriptHostManager>();
                Assert.Equal(ScriptHostState.Error, manager.State);
                Assert.IsType<HostInitializationException>(manager.LastError);
                Assert.Contains("%Cron%", manager.LastError.Message);

                // Check that one startup began successfully, then the restart was suppressed.
                var logMessages = fixture.Host.GetWebHostLogMessages();
                Assert.Single(logMessages, p => p.FormattedMessage != null && p.FormattedMessage.Contains("Building host: version spec: ~4, startup suppressed: 'True'"));
                Assert.Single(logMessages, p => p.FormattedMessage != null && p.FormattedMessage.Contains("Building host: version spec: ~4, startup suppressed: 'False'"));
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }


    }
}
