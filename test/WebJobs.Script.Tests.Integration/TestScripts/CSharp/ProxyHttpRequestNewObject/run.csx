
using System.Net;
using System.Net.Http.Headers;

public static async Task<HttpRequestMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    HttpRequestMessage req2 = new HttpRequestMessage();

    req2.Headers.Add("ServerDateTime", DateTime.Now.ToString());

    return req2;
}
