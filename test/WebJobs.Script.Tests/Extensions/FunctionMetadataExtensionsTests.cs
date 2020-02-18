// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class FunctionMetadataExtensionsTests
    {
        private readonly string _testRootScriptPath;

        public FunctionMetadataExtensionsTests()
        {
            _testRootScriptPath = Path.GetTempPath();
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("{ bindings: [ {} ] }")]
        [InlineData("{ bindings: 'not an array' }")]
        public async Task ToFunctionTrigger_InvalidConfig_ReturnsNull(string functionConfig)
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = "TestFunction"
            };
            var options = new ScriptJobHostOptions
            {
                RootScriptPath = _testRootScriptPath
            };
            var functionPath = Path.Combine(_testRootScriptPath, functionMetadata.Name);
            var functionMetadataFilePath = Path.Combine(functionPath, ScriptConstants.FunctionMetadataFileName);
            FileUtility.EnsureDirectoryExists(functionPath);
            File.WriteAllText(functionMetadataFilePath, functionConfig);

            var result = await functionMetadata.ToFunctionTrigger(options);
            Assert.Null(result);
        }

        [Fact]
        public void GetFunctionInvokeUrlTemplate_ReturnsExpectedResult()
        {
            string baseUrl = "https://localhost";
            var functionMetadata = new FunctionMetadata
            {
                Name = "TestFunction"
            };
            var httpTriggerBinding = new BindingMetadata
            {
                Name = "req",
                Type = "httpTrigger",
                Direction = BindingDirection.In,
                Raw = new JObject()
            };
            functionMetadata.Bindings.Add(httpTriggerBinding);
            var uri = WebHost.Extensions.FunctionMetadataExtensions.GetFunctionInvokeUrlTemplate(baseUrl, functionMetadata, "api");
            Assert.Equal("https://localhost/api/testfunction", uri.ToString());

            // with empty route prefix
            uri = WebHost.Extensions.FunctionMetadataExtensions.GetFunctionInvokeUrlTemplate(baseUrl, functionMetadata, string.Empty);
            Assert.Equal("https://localhost/testfunction", uri.ToString());

            // with a custom route
            httpTriggerBinding.Raw.Add("route", "catalog/products/{category:alpha?}/{id:int?}");
            uri = WebHost.Extensions.FunctionMetadataExtensions.GetFunctionInvokeUrlTemplate(baseUrl, functionMetadata, "api");
            Assert.Equal("https://localhost/api/catalog/products/{category:alpha?}/{id:int?}", uri.ToString());

            // with empty route prefix
            uri = WebHost.Extensions.FunctionMetadataExtensions.GetFunctionInvokeUrlTemplate(baseUrl, functionMetadata, string.Empty);
            Assert.Equal("https://localhost/catalog/products/{category:alpha?}/{id:int?}", uri.ToString());
        }
    }
}
