using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public static class CodelessEndToEndTests_Data
    {
        private readonly static string _sampleBindingsJson = $@"{{
          ""bindings"": [
            {{
              ""authLevel"": ""function"",
              ""type"": ""httpTrigger"",
              ""direction"": ""in"",
              ""name"": ""req"",
              ""methods"" : [
                ""get"",
                ""post""
              ]
            }},
            {{
              ""type"": ""http"",
              ""direction"": ""out"",
              ""name"": ""res""
            }}
          ]
        }}
        ";

        public static FunctionMetadata GetSampleMetadata(string functionName)
        {
            Func<HttpRequest, ILogger, Task<IActionResult>> invokeFunction = MyFunction;
            string endToendAssemblySuffix = "WebHostEndToEnd";

            var functionMetadata = new FunctionMetadata
            {
                Name = functionName,
                FunctionDirectory = null,
                ScriptFile = $"assembly:{Assembly.GetExecutingAssembly().FullName}",
                EntryPoint = $"{Assembly.GetExecutingAssembly().GetName().Name}.{endToendAssemblySuffix}.{typeof(CodelessEndToEndTests_Data).Name}.{invokeFunction.Method.Name}",
                Language = "DotNetAssembly"
            };

            JObject functionConfig = JObject.Parse(_sampleBindingsJson);
            JArray bindingArray = (JArray)functionConfig["bindings"];
            foreach (JObject binding in bindingArray)
            {
                BindingMetadata bindingMetadata = BindingMetadata.Create(binding);
                functionMetadata.Bindings.Add(bindingMetadata);
            }

            return functionMetadata;
        }

        public static Task<IActionResult> MyFunction(HttpRequest req, ILogger logger)
        {
            logger.LogInformation("Codeless Provider ran a function.");

            string name = req.Query["name"];

            string responseMessage = string.IsNullOrEmpty(name)
                ? "Codeless Provider ran a function successfully with no name parameter."
                : $"Hello, {name}! Codeless Provider ran a function successfully.";

            return Task.FromResult<IActionResult>(new OkObjectResult(responseMessage));
        }
    }
}
