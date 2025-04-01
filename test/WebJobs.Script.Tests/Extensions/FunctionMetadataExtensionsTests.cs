// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class FunctionMetadataExtensionsTests
    {
        private static readonly string _sampleBindingsJson = $@"{{
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
        public async Task ToFunctionTrigger_NoFile_ReturnsExpected()
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = "AnyFunction",
                EntryPoint = "MyEntry"
            };

            AddSampleBindings(functionMetadata);

            var options = new ScriptJobHostOptions
            {
                RootScriptPath = _testRootScriptPath
            };

            var result = await functionMetadata.ToFunctionTrigger(options);

            Assert.Equal("AnyFunction", result["functionName"].Value<string>());
            Assert.Equal("httpTrigger", result["type"].Value<string>());
        }

        [Fact]
        public async Task ToFunctionTrigger_Codeless_ReturnsExpected()
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = "TestFunction1"
            };
            var options = new ScriptJobHostOptions
            {
                RootScriptPath = _testRootScriptPath
            };

            functionMetadata.SetIsCodeless(true);

            AddSampleBindings(functionMetadata);

            var result = await functionMetadata.ToFunctionTrigger(options);
            Assert.Equal("TestFunction1", result["functionName"].Value<string>());
            Assert.Equal("httpTrigger", result["type"].Value<string>());

            // make sure original binding did not change
            Assert.Null(functionMetadata.Bindings[0].Raw["functionName"]?.Value<string>());
            Assert.Equal("httpTrigger", functionMetadata.Bindings[0].Raw["type"].Value<string>());
        }

        [Fact]
        public async Task ToFunctionMetadataResponse_WithoutFiles_ReturnsExpected()
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = "TestFunction1"
            };
            var options = new ScriptJobHostOptions
            {
                RootScriptPath = _testRootScriptPath
            };

            AddSampleBindings(functionMetadata);
            var result = await functionMetadata.ToFunctionMetadataResponse(options, string.Empty, null, excludeTestData: false);

            Assert.Null(result.ScriptRootPathHref);
            Assert.Null(result.ConfigHref);
            Assert.Equal("TestFunction1", result.Name);

            var binding = result.Config["bindings"] as JArray;
            Assert.Equal("httpTrigger", binding[0]["type"].Value<string>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ToFunctionMetadataResponse_TestData_Exclusion(bool shouldExcludeTestDataFromApiResponse)
        {
            var functionName = "TestFunction1";
            var functionMetadata = new FunctionMetadata
            {
                Name = functionName
            };
            var testDataFileName = $"{functionName}.dat";
            var testDataFilePath = Path.Combine(_testRootScriptPath, testDataFileName);
            var options = new ScriptJobHostOptions
            {
                RootScriptPath = _testRootScriptPath,
                TestDataPath = testDataFilePath
            };

            IFileSystem fileSystem = FileUtility.Instance;

            try
            {
                var testDataContent = @"
                                    {
                                        ""method"": ""POST"",
                                        ""headers"": [],
                                        ""body"": ""foo""
                                    }";

                var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(testDataContent))
                {
                    Position = 0
                };
                // Mock the file system
                var mockFileSystem = new Mock<IFileSystem>();
                var mockFile = new Mock<FileBase>();
                mockFileSystem.Setup(f => f.Directory.Exists(It.IsAny<string>())).Returns(true);
                mockFile.Setup(f => f.Exists(It.Is<string>(path => path.EndsWith(Path.Combine(functionName, "function.json"))))).Returns(true);
                mockFile.Setup(f => f.Exists(It.Is<string>(path => path.EndsWith(testDataFileName)))).Returns(true);
                mockFile.Setup(f => f.Open(It.Is<string>(path => path.EndsWith(testDataFileName)), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>()))
                        .Returns(memoryStream);
                mockFileSystem.Setup(fs => fs.File).Returns(mockFile.Object);
                FileUtility.Instance = mockFileSystem.Object;

                AddSampleBindings(functionMetadata);
                var result = await functionMetadata.ToFunctionMetadataResponse(options, string.Empty, null, excludeTestData: shouldExcludeTestDataFromApiResponse);

                Assert.Equal(functionName, result.Name);
                Assert.Equal(shouldExcludeTestDataFromApiResponse, result.TestData == null);
                if (!shouldExcludeTestDataFromApiResponse)
                {
                    Assert.True(result.TestData.Trim().Length > 0);
                }
            }
            finally
            {
                FileUtility.Instance = fileSystem;
            }
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

        private void AddSampleBindings(FunctionMetadata functionMetadata)
        {
            JObject functionConfig = JObject.Parse(_sampleBindingsJson);
            JArray bindingArray = (JArray)functionConfig["bindings"];
            foreach (JObject binding in bindingArray)
            {
                BindingMetadata bindingMetadata = BindingMetadata.Create(binding);
                functionMetadata.Bindings.Add(bindingMetadata);
            }
        }
    }
}
