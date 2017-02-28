using System.Net;

public static Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    var queryParams = req.GetQueryNameValuePairs()
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    log.Info(string.Format("C# HTTP trigger function processed a request. {0}", req.RequestUri));

    HttpResponseMessage res = null;
    string name;
    if (queryParams.TryGetValue("name", out name))
    {
        res = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Hello " + name)
        };
    }
    else
    {
        res = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Please pass a name on the query string")
        };
    }

    return Task.FromResult(res);
}