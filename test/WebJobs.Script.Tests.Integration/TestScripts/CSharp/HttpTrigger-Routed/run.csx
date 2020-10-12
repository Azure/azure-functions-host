#r "Newtonsoft.Json"

using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

public static async Task<IActionResult> Run(HttpRequest req, ILogger log)
{
    string action = req.Query["action"];
    switch (action)
    {
        case "accept":
            return new AcceptedAtRouteResult(@"httptrigger-redirect", new { param = "paramValue" }, new { @return = "accepted" });
        case "create":
            return new CreatedAtRouteResult(@"httptrigger-redirect", new { param = "paramValue" }, new { @return = "created" });
        case "acceptBadRoute":
            return new AcceptedAtRouteResult(@"AcceptedNonExistent", new { param = "paramValue" }, new { @return = "accepted" });
        case "createBadRoute":
            return new CreatedAtRouteResult(@"CreatedNonExistent", new { param = "paramValue" }, new { @return = "created" });
    }

    return new BadRequestResult();
}
