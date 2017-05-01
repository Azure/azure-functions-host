using System.Net;

public static Task<HttpResponseMessage> Run(HttpRequestMessage req, ExecutionContext context)
{
    req.Properties["ContextValue"] = context;

    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Empty)
    });
}