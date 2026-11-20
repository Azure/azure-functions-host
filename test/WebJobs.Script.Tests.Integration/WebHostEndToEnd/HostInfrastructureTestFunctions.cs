// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;

/// <summary>
/// Entry points for explicitly registered test extensions, never customer function metadata.
/// </summary>
public static class HostInfrastructureTestFunctions
{
    internal static FunctionMetadata Create(string name, string method, params JObject[] bindings)
    {
        var metadata = new FunctionMetadata
        {
            Name = name,
            ScriptFile = $"assembly:{typeof(HostInfrastructureTestFunctions).Assembly.FullName}",
            EntryPoint = $"{typeof(HostInfrastructureTestFunctions).FullName}.{method}",
            Language = DotNetScriptTypes.DotNetAssembly
        };
        foreach (JObject binding in bindings)
        {
            metadata.Bindings.Add(BindingMetadata.Create(binding));
        }

        return metadata;
    }

    internal static FunctionMetadata Http(string name, string method, string authLevel = "anonymous", string parameterName = "req")
    {
        return Create(name, method,
            new JObject
            {
                ["type"] = "httpTrigger", ["direction"] = "in", ["name"] = parameterName,
                ["authLevel"] = authLevel
            },
            new JObject { ["type"] = "http", ["direction"] = "out", ["name"] = "$return" });
    }

    internal static FunctionMetadata Manual()
    {
        return Create("Scenarios", nameof(ManualScenarios),
            new JObject { ["type"] = "manualTrigger", ["direction"] = "in", ["name"] = "input" });
    }

    internal static FunctionMetadata Timeout(string name)
    {
        return Create(name, name,
            new JObject
            {
                ["type"] = "manualTrigger", ["direction"] = "in",
                ["name"] = string.Equals(name, nameof(TimeoutSync), StringComparison.Ordinal) ? "inputData" : "input"
            });
    }

    /// <summary>Exercises the Host timeout for a blocking extension function.</summary>
    public static void TimeoutSync(string inputData, ILogger log)
    {
        log.LogInformation(inputData);
        Thread.Sleep(TimeSpan.FromSeconds(10));
    }

    /// <summary>Exercises cooperative and non-cooperative extension cancellation.</summary>
    public static async Task TimeoutToken(string input, ILogger log, CancellationToken token)
    {
        switch (input)
        {
            case "useToken":
                try
                {
                    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    log.LogInformation("Cancellation acknowledged.");
                }

                break;
            case "ignoreToken":
                await Task.Delay(TimeSpan.FromSeconds(15));
                break;
            default:
                throw new InvalidOperationException($"'{input}' is an unknown command.");
        }

        log.LogInformation("Done");
    }

    /// <summary>Exercises an HTTP invocation while the Host enters drain mode.</summary>
    public static async Task<IActionResult> Cancellation(HttpRequest req, ILogger log, CancellationToken token)
    {
        log.LogInformation("HTTP trigger cancellation function processing a request.");
        await Task.Delay(TimeSpan.FromSeconds(10), token);
        log.LogInformation("Function invocation successful.");
        return new OkResult();
    }

    public static async Task<IActionResult> HttpScenarios(HttpRequest req, ExecutionContext context, ILogger logger)
    {
        using var reader = new StreamReader(req.Body);
        JObject body = JObject.Parse(await reader.ReadToEndAsync());
        string scenario = (string)body["scenario"];
        switch (scenario)
        {
            case "staticWebApp":
                return new OkObjectResult(req.Query["code"].ToString());
            case "appServiceFixupMiddleware":
                return new OkObjectResult(req.GetDisplayUrl());
            case "appInsights-Success":
                LogPayload(body, context, logger);
                return new OkObjectResult(context.InvocationId.ToString());
            case "appInsights-Failure":
                LogPayload(body, context, logger);
                return new ConflictObjectResult(context.InvocationId.ToString());
            case "appInsights-Throw":
                LogPayload(body, context, logger);
                throw new InvalidOperationException(context.InvocationId.ToString());
            default:
                throw new InvalidOperationException();
        }
    }

    public static void ManualScenarios(JObject input, ExecutionContext context, ILogger logger)
    {
        LogPayload(input, context, logger);
        logger.LogMetric("TestMetric", 1234, new Dictionary<string, object>
        {
            ["Count"] = 50,
            ["Min"] = 10.4,
            ["Max"] = 23,
            ["MyCustomMetricProperty"] = 100
        });
    }

    public static IActionResult Redirect(HttpRequest req) => new RedirectResult("http://bing.com");

    public static IActionResult Routed(HttpRequest req)
    {
        return req.Query["action"].ToString() switch
        {
            "accept" => new AcceptedAtRouteResult("httptrigger-redirect", new { param = "paramValue" }, new { @return = "accepted" }),
            "create" => new CreatedAtRouteResult("httptrigger-redirect", new { param = "paramValue" }, new { @return = "created" }),
            "acceptBadRoute" => new AcceptedAtRouteResult("AcceptedNonExistent", new { param = "paramValue" }, new { @return = "accepted" }),
            "createBadRoute" => new CreatedAtRouteResult("CreatedNonExistent", new { param = "paramValue" }, new { @return = "created" }),
            _ => new BadRequestResult()
        };
    }

    public static IActionResult GetExecutionContext(HttpRequest req, ExecutionContext context) => new OkObjectResult(context);

    public static string DynamicBody(dynamic input) => $"Name: {input.name}, Location: {input.location}";

    public static TestPayload ModelBody(TestPayload payload) => payload;

    /// <summary>Exposes the Host-authenticated principal, including function-key identities.</summary>
    public static IActionResult Identities(HttpRequest req, ClaimsPrincipal principal)
    {
        string[] identities = principal.Identities.Select(identity =>
        {
            var userId = identity.FindFirst(ClaimTypes.NameIdentifier);
            return userId is not null
                ? $"Identity: ({identity.AuthenticationType}, {identity.FindFirst(ClaimTypes.Name).Value}, {userId.Value})"
                : $"Identity: ({identity.AuthenticationType}, {identity.FindFirst("http://schemas.microsoft.com/2017/07/functions/claims/authlevel").Value}, {identity.FindFirst("http://schemas.microsoft.com/2017/07/functions/claims/keyid").Value})";
        }).ToArray();

        return new OkObjectResult(string.Join(";", identities));
    }

    /// <summary>Exercises the Host's legacy request/response binding contract for extensions.</summary>
    public static HttpResponseMessage LegacyResponse(HttpRequestMessage req)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = req,
            Content = new StringContent("\"Hello from HttpResponseMessage\"", Encoding.UTF8, "application/json")
        };
    }

    /// <summary>Exercises the Host's typed HTTP input and object-result serialization.</summary>
    public static GreetingObject ObjectBody(GreetingObject req)
    {
        req.Greeting = $"Hello, {req.SenderName}";
        return req;
    }

    /// <summary>Exercises typed and separately named Host route-parameter bindings.</summary>
    public static ProductInfo RouteBinding(ProductInfo info, string category, int? id, string extra, ILogger logger)
    {
        logger.LogInformation($"ProductInfo: Category={info.Category} Id={info.Id}");
        logger.LogInformation($"Parameters: category={category} id={id} extra={extra}");
        return info;
    }

    public static IActionResult Invoke(HttpRequest req, ILogger logger)
    {
        logger.LogInformation("Test extension HTTP trigger function processed a request.");
        if (string.Equals(req.Query["action"], "throw", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Test exception");
        }

        using var reader = new StreamReader(req.Body);
        string body = reader.ReadToEnd();
        string name = req.Query["name"];
        if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(body))
        {
            name = (string)JObject.Parse(body)["name"];
        }

        return string.IsNullOrEmpty(name)
            ? new BadRequestObjectResult("Please pass a name on the query string or in the request body")
            : new OkObjectResult($"Hello, {name}!");
    }

    public static void CustomTrigger(string input)
    {
    }

    public static async Task QueueToBlob(WorkItem input, Stream output)
    {
        using var writer = new StreamWriter(output);
        await writer.WriteAsync($"{{ \"id\": \"{input.Id}\" }}");
    }

    private static void LogPayload(JObject body, ExecutionContext context, ILogger logger)
    {
        logger.LogInformation(JObject.FromObject(new
        {
            invocationId = context.InvocationId,
            trace = (string)body["value"]
        }).ToString(Formatting.None));
    }

    public sealed class WorkItem
    {
        public string Id { get; set; }

        public string Name { get; set; }
    }

    public sealed class TestPayload
    {
        public CustomType Custom { get; set; }

        public IEnumerable<CustomType> CustomEnumerable { get; set; }
    }

    public sealed class CustomType
    {
        public string CustomProperty { get; set; }
    }

    /// <summary>HTTP binding payload for the extension's object round-trip scenario.</summary>
    public sealed class GreetingObject
    {
        /// <summary>Gets or sets the request sender.</summary>
        public string SenderName { get; set; }

        /// <summary>Gets or sets the generated greeting.</summary>
        public string Greeting { get; set; }
    }

    /// <summary>HTTP route binding payload for the extension's optional parameters.</summary>
    public sealed class ProductInfo
    {
        /// <summary>Gets or sets the optional category.</summary>
        public string Category { get; set; }

        /// <summary>Gets or sets the optional product identifier.</summary>
        public int? Id { get; set; }
    }
}
