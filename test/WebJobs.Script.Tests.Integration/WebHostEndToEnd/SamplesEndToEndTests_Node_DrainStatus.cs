using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_DrainStatus : DrainTestFixture
    {
        [Fact]
        public async Task DrainStatus_RunningHost_ReturnsExpected()
        {
            // Validate the state is "Disabled" initially
            var response = await SamplesTestHelpers.InvokeDrainStatus(this);
            var responseString = response.Content.ReadAsStringAsync().Result;
            var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(status.State, DrainModeState.Disabled);

            ManualResetEvent resetEvent = new ManualResetEvent(false);
            _ = Task.Run(async () =>
            {
                // Put the host to drain mode
                response = await SamplesTestHelpers.InvokeDrain(this);

                // Validate the state is changed to "InProgress"
                await TestHelpers.Await(async () =>
                {
                    var response = await SamplesTestHelpers.InvokeDrainStatus(this);
                    var responseString = response.Content.ReadAsStringAsync().Result;
                    var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

                    return status.State == DrainModeState.InProgress;
                }, 20000);

                // Validate the state is changed to "Completed"
                await TestHelpers.Await(async () =>
                {
                    var response = await SamplesTestHelpers.InvokeDrainStatus(this);
                    var responseString = response.Content.ReadAsStringAsync().Result;
                    var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

                    return status.State == DrainModeState.Completed;
                }, 20000);

                resetEvent.Set();
            });

            response = await SamplesTestHelpers.InvokeHttpTrigger(this, "HttpTrigger-LongRun");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            bool result = resetEvent.WaitOne(30000);
            Assert.True(result);
        }   
    }

    public class DrainTestFixture : EndToEndTestFixture
    {
        static DrainTestFixture()
        {
        }

        public DrainTestFixture()
            : base(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", "NodeDrain"), "samples", RpcWorkerConstants.NodeLanguageWorkerName)
        {
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);
        }
    }
}
