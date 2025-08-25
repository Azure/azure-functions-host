using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TestFunctions
{
    public static class Functions
    {
        [FunctionName("Function1")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string action = req.Query["action"];
            if (action == "throw")
            {
                throw new InvalidOperationException("Kaboom!");
            }

            string name = req.Query["name"];

            // Explicitly read the body synchronously; this is used in the AllowSynchronousIOMiddleware tests
            string requestBody = new StreamReader(req.Body).ReadToEnd();

            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            return name != null
                ? (ActionResult)new OkObjectResult($"Hello, {name}!")
                : new BadRequestObjectResult("Please pass a name on the query string or in the request body");
        }

        [FunctionName("Function2")]
        public static IActionResult Function2([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string action = req.Query["action"];
            if (action == "throw")
            {
                throw new InvalidOperationException("Kaboom!");
            }

            string name = req.Query["name"];

            // Explicitly read the body synchronously; this is used in the AllowSynchronousIOMiddleware tests
            string requestBody = new StreamReader(req.Body).ReadToEnd();

            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            return name != null
                ? (ActionResult)new OkObjectResult($"Hello, {name}!")
                : new BadRequestObjectResult("Please pass a name on the query string or in the request body");
        }
    }
}