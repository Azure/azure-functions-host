using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

public static Task<HttpResponseMessage> HttpTrigger_CSharpScript(HttpRequestMessage req, TraceWriter log)
{
    log.Verbose("CSharp HTTP trigger function processed a request.");
    
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
}


