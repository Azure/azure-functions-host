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
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_Resume : ResumeTestFixture
    {
        [Fact]
        public async Task DrainModeEnabled_RunningHost_StartsNewHost_ReturnsOk()
        {
            // Validate the drain state is "Disabled" initially
            var response = await SamplesTestHelpers.InvokeDrainStatus(this);
            var responseString = response.Content.ReadAsStringAsync().Result;
            var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(status.State, DrainModeState.Disabled);

            ManualResetEvent resetEvent = new ManualResetEvent(false);
            _ = Task.Run(async () =>
            {
                // Put the host in drain mode
                response = await SamplesTestHelpers.InvokeDrain(this);

                // Validate the drain state is changed to "InProgress"
                await TestHelpers.Await(async () =>
                {
                    var response = await SamplesTestHelpers.InvokeDrainStatus(this);
                    var responseString = response.Content.ReadAsStringAsync().Result;
                    var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

                    return status.State == DrainModeState.InProgress;
                }, 20000);

                // Validate host is "Running" after resume is called
                await TestHelpers.Await(async () =>
                {
                    var response = await SamplesTestHelpers.InvokeResume(this);
                    var responseString = response.Content.ReadAsStringAsync().Result;
                    var status = JObject.Parse(responseString);

                    return (string)status["hostStatus"] == ScriptHostState.Running.ToString();
                }, 20000);

                // Validate the drain state is changed to "Disabled"
                await TestHelpers.Await(async () =>
                {
                    var response = await SamplesTestHelpers.InvokeDrainStatus(this);
                    var responseString = response.Content.ReadAsStringAsync().Result;
                    var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

                    return status.State == DrainModeState.Disabled;
                }, 20000);

                resetEvent.Set();
            });

            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bool result = resetEvent.WaitOne(30000);
            Assert.True(result);
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
            var resumeStatus = JObject.Parse(responseString);

            Assert.Equal(ScriptHostState.Running.ToString(), (string)resumeStatus["hostStatus"]);

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
