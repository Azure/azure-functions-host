using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.DrainModeEndToEnd)]
    public class DrainModeEndToEndTests : DrainTestFixture
    {
        [Fact]
        public async Task RunningHost_EnableDrainMode_ReturnsAccepted()
        {
            // Validate the drain state is "Disabled" initially
            var response = await SamplesTestHelpers.InvokeDrainStatus(this);
            var responseString = response.Content.ReadAsStringAsync().Result;
            var drainStatus = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(drainStatus.State, DrainModeState.Disabled);

            // Validate ability to call HttpTrigger without issues
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Put the host in drain mode and validate returns Accepted
            response = await SamplesTestHelpers.InvokeDrain(this);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // Validate the drain state is changed to "Completed"
            response = await SamplesTestHelpers.InvokeDrainStatus(this);
            responseString = response.Content.ReadAsStringAsync().Result;
            drainStatus = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(DrainModeState.Completed, drainStatus.State);

            // Validate HttpTrigger function is still working
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task RunningHost_EnableDrainMode_FunctionInvocationCancelled_ReturnsNotFound()
        {
            // Validate the drain state is "Disabled" initially
            var response = await SamplesTestHelpers.InvokeDrainStatus(this);
            var responseString = response.Content.ReadAsStringAsync().Result;
            var drainStatus = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(drainStatus.State, DrainModeState.Disabled);

            _ = Task.Run(async () =>
            {
                // Put the host in drain mode
                await TestHelpers.Await(async () =>
                {
                    response = await SamplesTestHelpers.InvokeDrain(this);
                    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                    return true;
                }, 10000);

            });

            // Call function with cancellation token handler
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger-Cancellation");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    public class DrainTestFixture : EndToEndTestFixture
    {
        static DrainTestFixture()
        {
        }

        public DrainTestFixture()
            : base(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", "CSharp"), "samples", RpcWorkerConstants.DotNetLanguageWorkerName)
        {
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);
        }
    }
}