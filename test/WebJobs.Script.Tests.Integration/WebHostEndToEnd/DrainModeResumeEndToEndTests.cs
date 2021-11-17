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
    public class DrainModeResumeEndToEndTests : ResumeTestFixture
    {
        [Fact]
        public async Task DrainModeEnabled_RunningHost_StartsNewHost_ReturnsOk()
        {
            // Validate the drain state is "Disabled" initially
            var response = await SamplesTestHelpers.InvokeDrainStatus(this);
            var responseString = response.Content.ReadAsStringAsync().Result;
            var drainStatus = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(drainStatus.State, DrainModeState.Disabled);

            // Get pre-drain instance Id
            // response = await SamplesTestHelpers.InvokeHostStatus(this);
            // responseString = response.Content.ReadAsStringAsync().Result;
            // var hostStatus = JsonConvert.DeserializeObject<HostStatus>(responseString);
            // var originalInstanceId = hostStatus.InstanceId;

            // Validate ability to call HttpTrigger without issues
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Put the host in drain mode
            response = await SamplesTestHelpers.InvokeDrain(this);

            // Validate the drain state is changed to "Completed"
            response = await SamplesTestHelpers.InvokeDrainStatus(this);
            responseString = response.Content.ReadAsStringAsync().Result;
            drainStatus = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(DrainModeState.Completed, drainStatus.State);

            // Validate host is "Running" after resume is called
            response = await SamplesTestHelpers.InvokeResume(this);
            responseString = response.Content.ReadAsStringAsync().Result;
            var hostStatus = JsonConvert.DeserializeObject<HostStatus>(responseString);

            Assert.Equal(ScriptHostState.Running.ToString(), hostStatus.State);

            // Validate the drain state is changed to "Disabled"
            response = await SamplesTestHelpers.InvokeDrainStatus(this);
            responseString = response.Content.ReadAsStringAsync().Result;
            drainStatus = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(DrainModeState.Disabled, drainStatus.State);

            // Get post-resume instance Id and verify it is different to the old instance Id
            // response = await SamplesTestHelpers.InvokeHostStatus(this);
            // responseString = response.Content.ReadAsStringAsync().Result;
            // hostStatus = JsonConvert.DeserializeObject<HostStatus>(responseString);
            // var newInstanceId = hostStatus.InstanceId;

            // Assert.NotEqual(originalInstanceId, newInstanceId);

            // Validate HttpTrigger function is still working
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task DrainModeDisabled_RunningHost_ReturnsOk()
        {
            // Validate the drain state is "Disabled" initially
            var response = await SamplesTestHelpers.InvokeDrainStatus(this);
            var responseString = response.Content.ReadAsStringAsync().Result;
            var drainStatus = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(drainStatus.State, DrainModeState.Disabled);

            // Validate host is "Running" after resume is called and drain mode is not active
            response = await SamplesTestHelpers.InvokeResume(this);
            responseString = response.Content.ReadAsStringAsync().Result;
            var hostStatus = JsonConvert.DeserializeObject<HostStatus>(responseString);

            Assert.Equal(ScriptHostState.Running.ToString(), hostStatus.State);

            // Validate HttpTrigger function is still working
            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    public class ResumeTestFixture : EndToEndTestFixture
    {
        static ResumeTestFixture()
        {
        }

        public ResumeTestFixture()
            : base(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", "NodeResume"), "samples", RpcWorkerConstants.NodeLanguageWorkerName)
        {
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);
        }
    }
}
