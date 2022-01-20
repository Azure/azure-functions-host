#r "Newtonsoft.Json"

using System;
using System.Net;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, ExecutionContext context, ILogger logger)
{
    string bodyJson = await req.Content.ReadAsStringAsync();
    JObject body = JObject.Parse(bodyJson);
    string scenario = body["scenario"].ToString();

    switch (scenario)
    {
        case "swa":
            var query = HttpUtility.ParseQueryString(req.RequestUri.Query ?? string.Empty);
            var code = query["code"];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(code) };
        case "appServiceFixupMiddleware":
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(req.RequestUri.ToString()) };
        case "appInsights-Success":
            LogPayload(body, context, logger);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(context.InvocationId.ToString()) };
        case "appInsights-Failure":
            LogPayload(body, context, logger);
            return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(context.InvocationId.ToString()) };
        case "appInsights-Throw":
            LogPayload(body, context, logger);
            throw new InvalidOperationException(context.InvocationId.ToString());
        default:
            throw new InvalidOperationException();
            break;
    }
}

private static void LogPayload(JObject body, ExecutionContext context, ILogger logger)
{
    string value = body["value"].ToString();
    string logPayload = JObject.FromObject(new { invocationId = context.InvocationId, trace = value }).ToString(Formatting.None);
    logger.LogInformation(logPayload);
}