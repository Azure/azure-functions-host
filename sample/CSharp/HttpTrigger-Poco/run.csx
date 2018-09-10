using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

public class RequestData
{
    public string Id { get; set; }
    public string Value { get; set; }
}

public static IActionResult Run(RequestData data, HttpRequest req, out string outBlob, ExecutionContext context, TraceWriter log)
{
    log.Info($"C# HTTP trigger function processed a request. {req.Host.Host}{req.Path}");
    log.Info($"InvocationId: {context.InvocationId}");

    outBlob = data.Value;

    return new OkResult();
}