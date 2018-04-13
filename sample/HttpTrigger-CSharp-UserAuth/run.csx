using System.Linq;
using System.Net;
using System.Security.Claims;

public static HttpResponseMessage Run(HttpRequestMessage req, ClaimsPrincipal principal, TraceWriter log)
{
    log.Info(string.Format("C# HTTP trigger function processed a request. {0}", req.RequestUri));

    string[] identityStrings = principal.Identities.Select(GetIdentityString).ToArray();

    return req.CreateResponse(HttpStatusCode.OK, string.Join(";", identityStrings));
}

private static string GetIdentityString(ClaimsIdentity identity)
{
    var userIdClaim = identity.FindFirst(ClaimTypes.NameIdentifier);
    if (userIdClaim != null)
    {
        // user identity
        var userNameClaim = identity.FindFirst(ClaimTypes.Name);
        return $"Identity: ({identity.AuthenticationType}, {userNameClaim.Value}, {userIdClaim.Value})";
    }
    else
    {
        // key based identity
        var authLevelClaim = identity.FindFirst("urn:functions:authLevel");
        var keyIdClaim = identity.FindFirst("urn:functions:keyId");
        return $"Identity: ({identity.AuthenticationType}, {authLevelClaim.Value}, {keyIdClaim.Value})";
    }
}