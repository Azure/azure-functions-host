using System.Net;

public static Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info("WarmUp function invoked.");

    var res = new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("WarmUp complete.")
    };

    return Task.FromResult(res);
}