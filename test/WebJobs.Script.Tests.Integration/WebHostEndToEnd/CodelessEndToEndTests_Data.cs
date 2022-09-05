using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

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

        public static FunctionMetadata GetCSharpSampleMetadata(string functionName)
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

        public static FunctionMetadata GetJavascriptSampleMetadata(string functionName)
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = functionName,
                FunctionDirectory = null,
                ScriptFile = $@"{GetFuntionSamplesPath("Node")}\HttpTrigger\index.js",
                EntryPoint = null,
                Language = "node"
            };

            JObject functionConfig = JObject.Parse(GetSamplesFunctionsJson("Node", "HttpTrigger"));
            JArray bindingArray = (JArray)functionConfig["bindings"];
            foreach (JObject binding in bindingArray)
            {
                BindingMetadata bindingMetadata = BindingMetadata.Create(binding);
                functionMetadata.Bindings.Add(bindingMetadata);
            }

            return functionMetadata;
        }

        public static FunctionMetadata GetJavaSampleMetadata(string functionName)
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = functionName,
                FunctionDirectory = null,
                ScriptFile = $@"{GetFuntionSamplesPath("Java")}\HttpTrigger\HttpTrigger-1.0-SNAPSHOT.jar",
                EntryPoint = "Microsoft.Azure.WebJobs.Script.Tests.EndToEnd.Function.run",
                Language = "java"
            };

            JObject functionConfig = JObject.Parse(GetSamplesFunctionsJson("Java", "HttpTrigger"));
            JArray bindingArray = (JArray)functionConfig["bindings"];
            foreach (JObject binding in bindingArray)
            {
                BindingMetadata bindingMetadata = BindingMetadata.Create(binding);
                functionMetadata.Bindings.Add(bindingMetadata);
            }

            return functionMetadata;
        }

        public static FunctionMetadata GetPowershellSampleMetadata(string functionName)
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = functionName,
                FunctionDirectory = null,
                ScriptFile = $@"{GetFuntionSamplesPath("powershell")}\HttpTrigger\run.ps1",
                EntryPoint = null,
                Language = "powershell"
            };

            JObject functionConfig = JObject.Parse(GetSamplesFunctionsJson("powershell", "HttpTrigger"));
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

        private static string GetFuntionSamplesPath(string runtime)
        {
            return Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", runtime);
        }

        private static string GetSamplesFunctionsJson(string runtime, string functionName)
        {
            return File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "sample", runtime, functionName, "function.json"));
        }
    }
}
