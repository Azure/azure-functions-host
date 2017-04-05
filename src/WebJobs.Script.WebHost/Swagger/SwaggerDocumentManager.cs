// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http.Routing;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Models.Swagger;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class SwaggerDocumentManager : ISwaggerDocumentManager
    {
        private readonly string _swaggerFilePath;
        private readonly string _swaggerDirPath;
        private readonly ScriptHostConfiguration _config;

        public SwaggerDocumentManager(ScriptHostConfiguration hostConfig)
        {
            _config = hostConfig;
            _swaggerDirPath = Path.Combine(_config.RootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName, ScriptConstants.SwaggerDirectoryName);
            _swaggerFilePath = Path.Combine(_swaggerDirPath, ScriptConstants.SwaggerFileName);
        }

        public async Task<JObject> GetSwaggerDocumentAsync() => await ReadSwaggerAsync();

        public JObject GenerateSwaggerDocument(IReadOnlyDictionary<IHttpRoute, FunctionDescriptor> httpFunctions)
        {
            var swaggerDocument = new SwaggerDocument();
            string hostname = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHostName);
            swaggerDocument.SwaggerInfo.FunctionAppName = hostname;
            swaggerDocument.Host = hostname;
            swaggerDocument.ApiEndpoints = GetEndpointsData(httpFunctions);
            return JObject.FromObject(swaggerDocument);
        }

        private static Dictionary<string, Dictionary<string, HttpOperationInfo>> GetEndpointsData(IReadOnlyDictionary<IHttpRoute, FunctionDescriptor> httpFunctions)
        {
            var apiEndpoints = new Dictionary<string, Dictionary<string, HttpOperationInfo>>();
            foreach (var httpRoute in httpFunctions.Keys)
            {
                if (httpFunctions[httpRoute].Metadata.IsDisabled == true)
                {
                    continue;
                }

                string endpoint = $"/{httpRoute.RouteTemplate}";
                Dictionary<string, HttpOperationInfo> endpointsOperationData;
                if (!apiEndpoints.TryGetValue(endpoint, out endpointsOperationData))
                {
                    endpointsOperationData = new Dictionary<string, HttpOperationInfo>();
                    apiEndpoints.Add(endpoint, endpointsOperationData);
                }

                string[] httpOperations = GetHttpOperations(httpRoute);
                ICollection<HttpOperationParameterInfo> inputParameters = GetInputParameters(httpRoute);
                foreach (var httpOperation in httpOperations)
                {
                    // Trace is not recogized as an HttpMethod by swagger specification
                    if (string.Equals(httpOperation, HttpMethod.Trace.ToString(), System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    HttpOperationInfo httpOperationInfo = new HttpOperationInfo(endpoint, httpOperation)
                    {
                        InputParameters = inputParameters
                    };
                    endpointsOperationData.Add(httpOperation, httpOperationInfo);
                }
            }
            return apiEndpoints;
        }

        private static string[] GetHttpOperations(IHttpRoute httpRoute)
        {
            HttpMethodConstraint httpMethodConstraint = null;
            if (httpRoute.Constraints.TryGetValue(ScriptConstants.HttpMethodConstraintName, out httpMethodConstraint) &&
                httpMethodConstraint.AllowedMethods != null)
            {
                return httpMethodConstraint.AllowedMethods.Select(p => p.Method.ToLowerInvariant()).ToArray<string>();
            }
            return ScriptConstants.HttpMethods.ToArray();
        }

        private static List<HttpOperationParameterInfo> GetInputParameters(IHttpRoute httpRoute)
        {
            var parameters = new List<HttpOperationParameterInfo>();
            var httpRouteFactory = new HttpRouteFactory();
            var pathParameters = httpRouteFactory.GetRouteParameters(httpRoute.RouteTemplate).ToList();

            foreach (var constraint in httpRoute.Constraints)
            {
                var httpConstraint = constraint.Value as IHttpRouteConstraint;
                SwaggerDataType? swaggerDataType = httpConstraint.ToSwaggerDataType();
                if (swaggerDataType != null && httpConstraint.GetType() != typeof(HttpMethodConstraint))
                {
                    var parameter = new HttpOperationParameterInfo()
                    {
                        DataType = swaggerDataType.ToString().ToLowerInvariant(),
                        Name = constraint.Key
                    };
                    parameters.Add(parameter);
                    pathParameters.Remove(constraint.Key);
                }
            }

            parameters.AddRange(pathParameters.Select(p =>
            new HttpOperationParameterInfo()
            {
                DataType = SwaggerDataType.String.ToString().ToLowerInvariant(),
                Name = p
            }));

            return parameters;
        }

        public async Task<bool> DeleteSwaggerDocumentAsync()
        {
            return await FileUtility.DeleteIfExistsAsync(_swaggerFilePath);
        }

        public async Task<JObject> AddOrUpdateSwaggerDocumentAsync(JObject swaggerDocumentJson)
        {
            FileUtility.EnsureDirectoryExists(_swaggerDirPath);
            await FileUtility.WriteAsync(_swaggerFilePath, swaggerDocumentJson.ToString());
            return await ReadSwaggerAsync();
        }

        private async Task<JObject> ReadSwaggerAsync()
        {
            JObject swagger = null;
            if (File.Exists(_swaggerFilePath))
            {
                string fileContent = await FileUtility.ReadAsync(_swaggerFilePath);
                swagger = JObject.Parse(fileContent);
            }
            return swagger;
        }
    }
}