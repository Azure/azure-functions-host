using System.Linq;
using System.Net;
using System.Security.Claims;

public static Task<HttpResponseMessage> Run(HttpRequestMessage req, ClaimsIdentity identity, TraceWriter log)
{
    log.Info(string.Format("C# HTTP trigger function processed a request. {0}", req.RequestUri));

    var authLevelClaim = identity.FindFirst("urn:functions:authLevel");
    var keyIdClaim = identity.FindFirst("urn:functions:keyId");
    log.Info($"Identity: ({identity.AuthenticationType}, {authLevelClaim.Value}, {keyIdClaim.Value})");

    var queryParams = req.GetQueryNameValuePairs()
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
    HttpResponseMessage res = null;
    if (queryParams.TryGetValue("name", out string name))
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