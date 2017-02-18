
using System.Net;
using System.Net.Http.Headers;

public static async Task<HttpRequestMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    req.Headers.Add("ServerDateTime", DateTime.Now.ToString());

    return req;
}
