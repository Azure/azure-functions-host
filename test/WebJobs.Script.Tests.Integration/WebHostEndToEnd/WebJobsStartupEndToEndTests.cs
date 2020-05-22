using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class WebJobsStartupEndToEndTests
    {
        [Fact]
        public async Task ExternalStartup_Succeeds()
        {
            // We need different fixture setup for each test.
            var fixture = new TestFixture();
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
            // We need different fixture setup for each test.
            var fixture = new TestFixture("* * * * * *"); // Startup.cs will change this.
            try
            {
                await fixture.InitializeAsync();
                var client = fixture.Host.HttpClient;

                var response = await client.GetAsync($"api/Function1");

                // The function does all the validation internally.
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

                var manager = fixture.Host.WebHostServices.GetService<IScriptHostManager>();
                Assert.Equal(ScriptHostState.Error, manager.State);
                Assert.IsType<HostInitializationException>(manager.LastError);
                Assert.Contains("%Cron%", manager.LastError.Message);

                // Check that one startup began successfully, then the restart was suppressed.
                var logMessages = fixture.Host.GetWebHostLogMessages();
                Assert.Single(logMessages, p => p.FormattedMessage != null && p.FormattedMessage.Contains("Building host: startup suppressed: 'True'"));
                Assert.Single(logMessages, p => p.FormattedMessage != null && p.FormattedMessage.Contains("Building host: startup suppressed: 'False'"));
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string TestPath = "..\\..\\..\\..\\WebJobsStartupTests\\bin\\netcoreapp3.1";
            private readonly IDisposable _dispose;

            public TestFixture(string cronExpression = "0 0 0 1 1 0") : base(TestPath, "webjobsstartup", "dotnet")
            {
                _dispose = new TestScopedEnvironmentVariable(new Dictionary<string, string>
                {
                    { "MyOptions__MyKey", "WillBeOverwrittenInAppStartup" },
                    { "MyOptions__MyOtherKey", "FromEnvironment" },
                    { "Cron", cronExpression }
                });
            }

            protected override Task CreateTestStorageEntities()
            {
                return Task.CompletedTask;
            }

            public override Task DisposeAsync()
            {
                _dispose.Dispose();
                return base.DisposeAsync();
            }
        }
    }
}
