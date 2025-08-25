using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

public static Task<HttpResponseMessage> Run(HttpRequestMessage req)
{
    // DO NOT modify this pattern as we want the test to run with it in place
    // to ensure it won't cause a deadlock.
    MyAsyncMethod().Wait();

    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
}

public static async Task MyAsyncMethod()
{
    await Task.Delay(500);
}