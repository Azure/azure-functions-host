#r "Newtonsoft.Json"

using System;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, ExecutionContext context, ILogger logger)
{
    string bodyJson = await req.Content.ReadAsStringAsync();
    JObject body = JObject.Parse(bodyJson);
    string scenario = body["scenario"].ToString();
    string value = body["value"].ToString();

    string logPayload = JObject.FromObject(new { invocationId = context.InvocationId, trace = value }).ToString(Formatting.None);

    switch (scenario)
    {
        case "appInsights-Success":
            logger.LogInformation(logPayload);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(context.InvocationId.ToString()) };
        case "appInsights-Failure":
            logger.LogInformation(logPayload);
            return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(context.InvocationId.ToString()) };
        case "appInsights-Throw":
            logger.LogInformation(logPayload);
            throw new InvalidOperationException(context.InvocationId.ToString());
        default:
            throw new InvalidOperationException();
            break;
    }    
}