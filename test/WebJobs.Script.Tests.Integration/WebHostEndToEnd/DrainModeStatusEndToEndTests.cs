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
    [Trait(TestTraits.Group, TestTraits.DrainModeEndToEnd)]
    public class DrainModeStatusEndToEndTests : DrainStatusTestFixture
    {
        [Fact]
        public async Task DrainStatus_RunningHost_ReturnsExpected()
        {
            // Validate the state is "Disabled" initially
            var response = await SamplesTestHelpers.InvokeDrainStatus(this);
            var responseString = response.Content.ReadAsStringAsync().Result;
            var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(DrainModeState.Disabled, status.State);

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

    public class DrainStatusTestFixture : EndToEndTestFixture
    {
        static DrainStatusTestFixture()
        {
        }

        public DrainStatusTestFixture()
            : base(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", "NodeDrain"), "samples", RpcWorkerConstants.NodeLanguageWorkerName)
        {
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);
        }
    }
}
