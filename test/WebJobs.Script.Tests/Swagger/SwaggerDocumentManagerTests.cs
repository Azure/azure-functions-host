// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Routing;
using System.Web.Http.Routing.Constraints;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Models.Swagger;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SwaggerDocumentManagerTests
    {
        private ScriptHostConfiguration _scriptConfig;

        public SwaggerDocumentManagerTests()
        {
            _scriptConfig = new ScriptHostConfiguration();
        }

        [Fact]
        public async Task GetSwaggerDocumentAsync_ReturnsSwaggerDocument()
        {
            using (var directory = new TempDirectory())
            using (var systemDirectoryName = new TempDirectory(Path.Combine(directory.Path, ScriptConstants.AzureFunctionsSystemDirectoryName)))
            using (var swaggerDirectoryName = new TempDirectory(Path.Combine(systemDirectoryName.Path, ScriptConstants.SwaggerDirectoryName)))
            {
                string swaggerDocument = @"{}";
                File.WriteAllText(Path.Combine(swaggerDirectoryName.Path, ScriptConstants.SwaggerFileName), swaggerDocument);
                _scriptConfig.RootScriptPath = directory.Path;
                var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);
                var document = await swaggerDocumentManager.GetSwaggerDocumentAsync();
                Assert.Equal(JObject.Parse(swaggerDocument), document);
            }
        }

        [Fact]
        public async Task GetSwaggerDocumentAsync_ReturnsNull_WhenFileIsNotPresent()
        {
            using (var directory = new TempDirectory())
            {
                JObject emptyObject = null;
                _scriptConfig.RootScriptPath = directory.Path;
                var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);
                var document = await swaggerDocumentManager.GetSwaggerDocumentAsync();
                Assert.Equal(emptyObject, document);
            }
        }

        [Fact]
        public async Task DeleteSwaggerDocumentAsync_ReturnsFalse_WhenFileIsNotPresent()
        {
            using (var directory = new TempDirectory())
            {
                _scriptConfig.RootScriptPath = directory.Path;
                var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);
                var deleteResult = await swaggerDocumentManager.DeleteSwaggerDocumentAsync();
                Assert.Equal(false, deleteResult);
            }
        }

        [Fact]
        public async Task DeleteSwaggerDocumentAsync_ReturnsTrue_WhenIsDeleted()
        {
            using (var directory = new TempDirectory())
            using (var systemDirectoryName = new TempDirectory(Path.Combine(directory.Path, ScriptConstants.AzureFunctionsSystemDirectoryName)))
            using (var swaggerDirectoryName = new TempDirectory(Path.Combine(systemDirectoryName.Path, ScriptConstants.SwaggerDirectoryName)))
            {
                string swaggerDocument = @"{}";
                File.WriteAllText(Path.Combine(swaggerDirectoryName.Path, ScriptConstants.SwaggerFileName), swaggerDocument);
                _scriptConfig.RootScriptPath = directory.Path;
                var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);
                var deleteResult = await swaggerDocumentManager.DeleteSwaggerDocumentAsync();
                Assert.Equal(true, deleteResult);
            }
        }

        [Fact]
        public async Task AddOrUpdateSwaggerDocumentAsync_CreatesDirectoryPathAndWritesToFile()
        {
            using (var directory = new TempDirectory())
            {
                string swaggerDocument = @"{
                                            'swagger': '2.0',
                                                'info': {
                                                    'title': 'localhost',
                                                    'version': '1.0.0'
                                                    }
                                            }";
                _scriptConfig.RootScriptPath = directory.Path;
                var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);
                var updatedContent = await swaggerDocumentManager.AddOrUpdateSwaggerDocumentAsync(JObject.Parse(swaggerDocument));
                string swaggerFilePath = Path.Combine(directory.Path, ScriptConstants.AzureFunctionsSystemDirectoryName, ScriptConstants.SwaggerDirectoryName, ScriptConstants.SwaggerFileName);
                string updatedFile = File.ReadAllText(swaggerFilePath);
                Assert.Equal(updatedContent, JObject.Parse(updatedFile));
            }
        }

        [Fact]
        public void GenerateSwaggerDocument_CreatesBasicSwaggerDocument()
        {
            string apiEndpoint = "/api/HttpTriggerCSharp1";
            string routeTemplate = apiEndpoint.Substring(1);

            HttpMethod[] allowedMethods = { HttpMethod.Get };
            var httpMethodConstraint = new HttpMethodConstraint(allowedMethods);

            HttpRouteValueDictionary constraints = new HttpRouteValueDictionary();
            constraints.Add(ScriptConstants.HttpMethodConstraintName, httpMethodConstraint);

            var function = new FunctionDescriptor("HttpTriggerCSharp1", null, new FunctionMetadata(), null, null, null, null);
            var dataTokens = new Dictionary<string, object>
            {
                { ScriptConstants.AzureFunctionsHttpFunctionKey, function }
            };
            HttpRouteCollection routes = new HttpRouteCollection();
            var route = routes.CreateRoute(routeTemplate, null, constraints, dataTokens);
            routes.Add("route1", route);

            // Act
            var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);

            var generatedDocument = swaggerDocumentManager.GenerateSwaggerDocument(routes);

            string hostName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName);
            if (hostName == null)
            {
                generatedDocument["info"]["title"] = string.Empty;
                generatedDocument["host"] = string.Empty;
            }

            string expectedSwagger = @"
{
    'swagger': '2.0',
    'info': {
        'title': 'localhost',
        'version': '1.0.0'
    },
    'host': 'localhost',
    'basePath': '/',
    'schemes': [
        'https',
        'http'
    ],
    'paths': {
        '/api/HttpTriggerCSharp1': {
            'get': {
                'operationId': '/api/HttpTriggerCSharp1/get',
                'produces': [],
                'consumes': [],
                'parameters': [],
                'description': 'Replace with Operation Object #http://swagger.io/specification/#operationObject',
                'responses': {
                    '200': {
                        'description': 'Success operation'
                    }
                },
                'security': [
                    {
                        'apikeyQuery': []
    }
                ]
            }
        }
    },
    'definitions': {},
    'securityDefinitions': {
        'apikeyQuery': {
            'type': 'apiKey',
            'name': 'code',
            'in': 'query'
        }
    }
}".Replace("localhost", hostName);

            expectedSwagger = JObject.Parse(expectedSwagger).ToString(Formatting.None);
            Assert.Equal(expectedSwagger, generatedDocument.ToString(Formatting.None));
        }

        [Fact]
        public void GenerateSwaggerDocument_CreatesSwaggerDocument_WithAllHttpMethods()
        {
            string apiEndpoint = "/api/HttpTriggerCSharp1";
            var routeTemplate = apiEndpoint.Substring(1);
            var function = new FunctionDescriptor("HttpTriggerCSharp1", null, new FunctionMetadata(), null, null, null, null);
            var dataTokens = new Dictionary<string, object>
            {
                { ScriptConstants.AzureFunctionsHttpFunctionKey, function }
            };
            HttpRouteCollection routes = new HttpRouteCollection();
            var route = routes.CreateRoute(routeTemplate, null, null, dataTokens);
            routes.Add("route1", route);

            var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);
            var generatedDocument = swaggerDocumentManager.GenerateSwaggerDocument(routes);
            var swaggerdoc = generatedDocument.ToObject<SwaggerDocument>();
            Assert.True(swaggerdoc.ApiEndpoints.ContainsKey(apiEndpoint));
            Assert.Equal(swaggerdoc.ApiEndpoints.Keys.Count, 1);

            var httpOperations = swaggerdoc.ApiEndpoints[apiEndpoint];
            Assert.Equal(httpOperations.Count, ScriptConstants.HttpMethods.Length);
            foreach (var httpMethod in ScriptConstants.HttpMethods)
            {
                Assert.True(httpOperations.ContainsKey(httpMethod));
                Assert.NotNull(httpOperations[httpMethod]);
            }
        }

        [Fact]
        public void GenerateSwaggerDocument_CreatesSwaggerDocument_WithSelectHttpMethods()
        {
            // Arrange
            string apiEndpoint = "/api/HttpTriggerCSharp1";
            string routeTemplate = apiEndpoint.Substring(1);

            HttpMethod[] allowedMethods = { HttpMethod.Get, HttpMethod.Post };
            var httpMethodConstraint = new HttpMethodConstraint(allowedMethods);

            HttpRouteValueDictionary constraints = new HttpRouteValueDictionary();
            constraints.Add(ScriptConstants.HttpMethodConstraintName, httpMethodConstraint);

            var function = new FunctionDescriptor("HttpTriggerCSharp1", null, new FunctionMetadata(), null, null, null, null);
            var dataTokens = new Dictionary<string, object>
            {
                { ScriptConstants.AzureFunctionsHttpFunctionKey, function }
            };
            HttpRouteCollection routes = new HttpRouteCollection();
            var route = routes.CreateRoute(routeTemplate, null, constraints, dataTokens);
            routes.Add("route1", route);

            var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);

            // Act
            var generatedDocument = swaggerDocumentManager.GenerateSwaggerDocument(routes);

            // Assert
            var swaggerdoc = generatedDocument.ToObject<SwaggerDocument>();
            Assert.True(swaggerdoc.ApiEndpoints.ContainsKey(apiEndpoint));
            Assert.Equal(swaggerdoc.ApiEndpoints.Keys.Count, 1);

            var httpOperations = swaggerdoc.ApiEndpoints[apiEndpoint];
            Assert.Equal(httpOperations.Count, 2);
            foreach (var httpMethod in httpMethodConstraint.AllowedMethods)
            {
                Assert.True(httpOperations.ContainsKey(httpMethod.Method.ToString().ToLowerInvariant()));
                Assert.NotNull(httpOperations[httpMethod.Method.ToString().ToLowerInvariant()]);
            }
        }

        [Fact]
        public void GenerateSwaggerDocument_ExcludesDisabledMethods()
        {
            // Arrange
            string apiEndpoint = "/api/HttpTriggerCSharp1";
            string routeTemplate = apiEndpoint.Substring(1);
            var disabledFunction = new FunctionMetadata() { IsDisabled = true };

            var function = new FunctionDescriptor("HttpTriggerCSharp1", null, disabledFunction, null, null, null, null);
            var dataTokens = new Dictionary<string, object>
            {
                { ScriptConstants.AzureFunctionsHttpFunctionKey, function }
            };
            HttpRouteCollection routes = new HttpRouteCollection();
            var route = routes.CreateRoute(routeTemplate, null, null, dataTokens);
            routes.Add("route1", route);

            var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);

            // Act
            var generatedDocument = swaggerDocumentManager.GenerateSwaggerDocument(routes);

            // Assert
            var swaggerdoc = generatedDocument.ToObject<SwaggerDocument>();
            Assert.Equal(swaggerdoc.ApiEndpoints.Keys.Count, 0);
        }

        [Fact]
        public void GenerateSwaggerDocument_AddsParameterInfoUsingHttpRouteConstraintAndSelectMethods()
        {
            // Arrange
            string apiEndpoint = "/api/{id}/HttpTriggerCSharp1";
            string routeTemplate = apiEndpoint.Substring(1);

            HttpMethod[] allowedMethods = { HttpMethod.Get, HttpMethod.Post };
            var httpMethodConstraint = new HttpMethodConstraint(allowedMethods);

            HttpRouteValueDictionary constraints = new HttpRouteValueDictionary();
            constraints.Add(ScriptConstants.HttpMethodConstraintName, httpMethodConstraint);
            constraints.Add("id", new IntRouteConstraint());

            var function = new FunctionDescriptor("HttpTriggerCSharp1", null, new FunctionMetadata(), null, null, null, null);
            var dataTokens = new Dictionary<string, object>
            {
                { ScriptConstants.AzureFunctionsHttpFunctionKey, function }
            };
            HttpRouteCollection routes = new HttpRouteCollection();
            var route = routes.CreateRoute(routeTemplate, null, constraints, dataTokens);
            routes.Add("route1", route);

            var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);

            // Act
            var generatedDocument = swaggerDocumentManager.GenerateSwaggerDocument(routes);

            // Assert
            var swaggerdoc = generatedDocument.ToObject<SwaggerDocument>();
            Assert.True(swaggerdoc.ApiEndpoints.ContainsKey(apiEndpoint));
            Assert.Equal(swaggerdoc.ApiEndpoints.Keys.Count, 1);

            var httpOperations = swaggerdoc.ApiEndpoints[apiEndpoint];
            Assert.Equal(httpOperations.Count, 2);

            foreach (var httpMethod in httpMethodConstraint.AllowedMethods)
            {
                string httpMethodName = httpMethod.Method.ToString().ToLowerInvariant();

                Assert.True(httpOperations.ContainsKey(httpMethodName));
                Assert.NotNull(httpOperations[httpMethodName]);
                var inputParams = httpOperations[httpMethodName].InputParameters.ToList();
                Assert.Equal(inputParams.Count, 1);
                Assert.Equal(inputParams[0].DataType, SwaggerDataType.Integer.ToString().ToLowerInvariant());
            }
        }

        [Fact]
        public void GenerateSwaggerDocument_AddsParameterInfoUsingKnownAndUnknownHttpRouteConstraint()
        {
            // Arrange
            string apiEndpoint = "/api/{id}/{category}/HttpTriggerCSharp1";
            string routeTemplate = apiEndpoint.Substring(1);

            HttpMethod[] allowedMethods = { HttpMethod.Get, HttpMethod.Post };
            var httpMethodConstraint = new HttpMethodConstraint(allowedMethods);

            HttpRouteValueDictionary constraints = new HttpRouteValueDictionary();
            constraints.Add(ScriptConstants.HttpMethodConstraintName, httpMethodConstraint);
            constraints.Add("id", new IntRouteConstraint());

            var function = new FunctionDescriptor("HttpTriggerCSharp1", null, new FunctionMetadata(), null, null, null, null);
            var dataTokens = new Dictionary<string, object>
            {
                { ScriptConstants.AzureFunctionsHttpFunctionKey, function }
            };
            HttpRouteCollection routes = new HttpRouteCollection();
            var route = routes.CreateRoute(routeTemplate, null, constraints, dataTokens);
            routes.Add("route1", route);

            var swaggerDocumentManager = new SwaggerDocumentManager(_scriptConfig);

            // Act
            var generatedDocument = swaggerDocumentManager.GenerateSwaggerDocument(routes);

            // Assert
            var swaggerdoc = generatedDocument.ToObject<SwaggerDocument>();
            Assert.True(swaggerdoc.ApiEndpoints.ContainsKey(apiEndpoint));
            Assert.Equal(swaggerdoc.ApiEndpoints.Keys.Count, 1);

            var httpOperations = swaggerdoc.ApiEndpoints[apiEndpoint];
            Assert.Equal(httpOperations.Count, 2);

            foreach (var httpMethod in httpMethodConstraint.AllowedMethods)
            {
                Assert.True(httpOperations.ContainsKey(httpMethod.Method.ToString().ToLowerInvariant()));
                Assert.NotNull(httpOperations[httpMethod.Method.ToString().ToLowerInvariant()]);

                var inputParams = httpOperations[httpMethod.Method.ToString().ToLowerInvariant()].InputParameters.ToList();
                Assert.Equal(inputParams.Count, 2);
                Assert.Equal(inputParams[0].DataType, SwaggerDataType.Integer.ToString().ToLowerInvariant());
                Assert.Equal(inputParams[1].DataType, SwaggerDataType.String.ToString().ToLowerInvariant());
            }
        }
    }
}
