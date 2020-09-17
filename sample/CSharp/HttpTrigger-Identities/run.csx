using System.Linq;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

public static IActionResult Run(HttpRequest req, TraceWriter log, ClaimsPrincipal principal)
{
    log.Info("C# HTTP trigger function processed a request.");

    string[] identityStrings = principal.Identities.Select(GetIdentityString).ToArray();

    return new OkObjectResult(string.Join(";", identityStrings));
}

private static string GetIdentityString(ClaimsIdentity identity)
{
    var userIdClaim = identity.FindFirst(ClaimTypes.NameIdentifier);
    if (userIdClaim != null)
    {
        // user identity
        var userNameClaim = identity.FindFirst(ClaimTypes.Name);

        // easy auth identity if no roles exist
        var rolesClaims = identity.FindAll(ClaimTypes.Role)?.ToList();
        if (rolesClaims == null || rolesClaims.Count == 0)
        {
            return $"Identity: ({identity.AuthenticationType}, {userNameClaim.Value}, {userIdClaim.Value})";
        }

        // static web app identity
        List<string> roles = rolesClaims.Select(roleClaim => roleClaim.Value).ToList();
        string rolesString = $"[{string.Join(",", roles)}]";
        return $"Static Web Apps Identity: ({identity.AuthenticationType}, {userIdClaim.Value}, {userNameClaim.Value}, {rolesString})";
    }
    else
    {
        // key based identity
        var authLevelClaim = identity.FindFirst("http://schemas.microsoft.com/2017/07/functions/claims/authlevel");
        var keyIdClaim = identity.FindFirst("http://schemas.microsoft.com/2017/07/functions/claims/keyid");
        return $"Identity: ({identity.AuthenticationType}, {authLevelClaim.Value}, {keyIdClaim.Value})";
    }
}