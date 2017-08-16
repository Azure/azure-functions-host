using System.Linq;
using System.Net;
using System.Security.Claims;

public static Task<HttpResponseMessage> Run(HttpRequestMessage req, ClaimsIdentity identity, TraceWriter log)
{
    log.Info(string.Format("C# HTTP trigger function processed a request. {0}", req.RequestUri));

    string responseContent = null;
    var userIdClaim = identity.FindFirst(ClaimTypes.NameIdentifier);
    if (userIdClaim != null)
    {
        // user identity
        var userNameClaim = identity.FindFirst(ClaimTypes.Name);
        responseContent = $"Identity: ({identity.AuthenticationType}, {userNameClaim.Value}, {userIdClaim.Value})";
    }
    else
    {
        // key based identity
        var authLevelClaim = identity.FindFirst("urn:functions:authLevel");
        var keyIdClaim = identity.FindFirst("urn:functions:keyId");
        responseContent = $"Identity: ({identity.AuthenticationType}, {authLevelClaim.Value}, {keyIdClaim.Value})";
    }
    
    var res = new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(responseContent)
    };
    
    return Task.FromResult(res);
}