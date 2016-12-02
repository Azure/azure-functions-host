#load "Class.csx"

using System.Net;

public static Task<HttpResponseMessage> Run(HttpRequestMessage req)
{
    string response = new Test().Response;
    req.Properties["LoadedScriptResponse"] = response;

    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(response)
    });
}