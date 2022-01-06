using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System.Web.Http;

public static HttpResponseMessage Run(HttpRequestMessage req, ILogger log)
{
    log.LogInformation("C# HTTP trigger with legacy types function processed a request.");

    return req.CreateResponse(System.Net.HttpStatusCode.OK, "Hello from HttpResponseMessage");
}