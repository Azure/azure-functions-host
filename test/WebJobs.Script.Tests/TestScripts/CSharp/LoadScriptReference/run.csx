#load "Class.csx"

using System.Net;
using System.Diagnostics;

public static Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    string response = new Test().Response;
    req.Properties["LoadedScriptResponse"] = response;

    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(response)
    });
}