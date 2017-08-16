#r "Newtonsoft.Json"

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Claims;

public static JObject Run(Payload payload, string action, ClaimsIdentity identity, TraceWriter log)
{
    var authLevelClaim = identity.FindFirst("urn:functions:authLevel");
    var keyIdClaim = identity.FindFirst("urn:functions:keyId");
    log.Info($"Identity: ({identity.AuthenticationType}, {authLevelClaim.Value}, {keyIdClaim.Value})");

    JObject body = new JObject()
    {
        { "result", $"Value: {payload.Value} Action: {action}" }
    };
    return body;
}

public class Payload
{
    public string Id { get; set; }
    public string Value { get; set; }
}
