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
            var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(status.State, DrainModeState.Disabled);

            ManualResetEvent resetEvent = new ManualResetEvent(false);
            _ = Task.Run(async () =>
            {
                // Validate host is "Running" after resume is called and drain mode is not active
                await TestHelpers.Await(async () =>
                {
                    var response = await SamplesTestHelpers.InvokeResume(this);
                    var responseString = response.Content.ReadAsStringAsync().Result;
                    var status = JObject.Parse(responseString);

                    return (string)status["hostStatus"] == ScriptHostState.Running.ToString();
                }, 20000);

                resetEvent.Set();
            });

            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bool result = resetEvent.WaitOne(30000);
            Assert.True(result);
        }
        // [Fact]
        // public async Task HostOffline_ReturnsServiceUnavailable()
        // {
        //     // Take host offline
        //     await SamplesTestHelpers.SetHostStateAsync(this, "offline");

        //     ManualResetEvent resetEvent = new ManualResetEvent(false);
        //     _ = Task.Run(async () =>
        //     {
        //         // Validate we get a 503 if we call resume when the host is not running
        //         await TestHelpers.Await(async () =>
        //         {
        //             var response = await SamplesTestHelpers.InvokeResume(this);

        //             return response.StatusCode == HttpStatusCode.ServiceUnavailable;
        //         }, 20000);

        //         resetEvent.Set();
        //     });

        //     May need to reinitialize TestFunctionHost to reset IApplicationLifetime
            //  await fixture.InitializeAsync();
        //     var response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger");
        //     Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        //     bool result = resetEvent.WaitOne(30000);
        //     Assert.True(result);
        // }
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
