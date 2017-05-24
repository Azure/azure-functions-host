// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    // Running these in their own class with private fixtures so they do not affect the timing of
    // subsequent tests.
    public class SamplesEndToEndRestartTests
    {
        [Fact(Skip = "Will not pass until https://github.com/Azure/azure-webjobs-sdk-script/issues/1537 is fixed")]
        public async Task HttpTrigger_HostRestarts()
        {
            string uri = "api/httptrigger-csharp?name=brett";

            bool stop = false;
            bool failed = false;
            HttpResponseMessage failedResponse = null;

            // Use a private fixture to prevent timing restart issues with other tests
            SamplesEndToEndTests.TestFixture fixture = new SamplesEndToEndTests.TestFixture();
            string hostJsonPath = Path.Combine(fixture.HostSettings.ScriptPath, "host.json");
            string originalHostJson = File.ReadAllText(hostJsonPath);
            try
            {
                // setup just this function to keep things quicker
                JObject hostJson = JObject.Parse(originalHostJson);
                hostJson["functions"] = new JArray("HttpTrigger-CSharp");
                File.WriteAllText(hostJsonPath, hostJson.ToString());

                List<Task> requestTasks = new List<Task>();

                for (int i = 0; i < 5; i++)
                {
                    requestTasks.Add(Task.Run(() =>
                    {
                        while (!failed && !stop)
                        {
                            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                            request.Headers.Add("x-functions-key", "t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy");
                            HttpResponseMessage response = fixture.HttpClient.SendAsync(request).Result;
                            if (!response.IsSuccessStatusCode)
                            {
                                failed = true;
                                failedResponse = response;
                            }
                        }
                    }));
                }

                string functionJsonPath = Path.Combine(fixture.HostSettings.ScriptPath, "HttpTrigger-CSharp", "function.json");

                // try for 2 minutes to force a host restart
                for (int i = 0; i < 60 && !failed; i++)
                {
                    await Task.Delay(2000);
                    File.SetLastWriteTimeUtc(functionJsonPath, DateTime.UtcNow);
                }

                stop = true;
                await Task.WhenAll(requestTasks);

                Assert.False(failed, $"Request failed. Response: {failedResponse?.StatusCode} {failedResponse?.Content.ReadAsStringAsync().Result}");
            }
            finally
            {
                File.WriteAllText(hostJsonPath, originalHostJson);
                fixture.Dispose();
            }
        }
    }
}
