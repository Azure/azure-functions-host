using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(WebJobsStartupEndToEndTests))]
    public class WebJobsStartupEndToEndTests : EndToEndTestsBase<WebJobsStartupEndToEndTests.TestFixture>
    {
        public WebJobsStartupEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task Loads()
        {
            var client = Fixture.Host.HttpClient;

            using (new TestScopedEnvironmentVariable("MyOptions__MyKey", "WillBeOverwrittenInAppStartup"))
            {
                var response = await client.GetAsync($"api/Function1");

                // The function does all the validation internally.
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string TestPath = "..\\..\\..\\..\\WebJobsStartupTests\\bin\\netcoreapp2.2";
            private readonly IDisposable _dispose;

            public TestFixture() : base(TestPath, "webjobsstartup", "dotnet")
            {
                _dispose = new TestScopedEnvironmentVariable(new Dictionary<string, string>
                {
                    { "MyOptions__MyKey", "WillBeOverwrittenInAppStartup" },
                    { "MyOptions__MyOtherKey", "FromEnvironment" }                   
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
