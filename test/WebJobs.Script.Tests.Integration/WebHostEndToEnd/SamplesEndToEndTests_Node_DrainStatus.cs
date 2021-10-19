using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_DrainStatus : SamplesEndToEndTests_Node_Retry.TestFixture
    {
        [Theory]
        [InlineData("HttpTrigger-RetryStatus-Fixed")]
        [InlineData("HttpTrigger-RetryStatus-Exponential")]
        public async Task DrainStatus_ReturnsExpected(string functionName)
        {
            // Validate the state is to "Disabled" initially
            var response = await SamplesTestHelpers.InvokeDrainStatus(this);
            var responseString = response.Content.ReadAsStringAsync().Result;
            var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

            Assert.Equal(status.State, DrainModeState.Disabled);

            // Put the host to drain mode
            response = await SamplesTestHelpers.InvokeDrain(this);

            ManualResetEvent resetEvent = new ManualResetEvent(false);
            _ = Task.Run(async () =>
            {
                // Validate the state is changed to "InProgress"
                await TestHelpers.Await(async () =>
                {
                    var response = await SamplesTestHelpers.InvokeDrainStatus(this);
                    var responseString = response.Content.ReadAsStringAsync().Result;
                    var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

                    return status.State == DrainModeState.InProgress;
                }, 20000, 200);

                // Validate the state is changed to "Completed"
                await TestHelpers.Await(async () =>
                {
                    var response = await SamplesTestHelpers.InvokeDrainStatus(this);
                    var responseString = response.Content.ReadAsStringAsync().Result;
                    var status = JsonConvert.DeserializeObject<DrainModeStatus>(responseString);

                    return status.State == DrainModeState.Completed;
                }, 20000, 200);

                resetEvent.Set();
            });

            response = await SamplesTestHelpers.InvokeHttpTrigger(this, functionName);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            bool result = resetEvent.WaitOne(30000);
            Assert.True(result);
        }

    }
}
