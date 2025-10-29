// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    public static class HttpScriptInvocationResultExtensions
    {
        public static ScriptInvocationResult ToScriptInvocationResult(this HttpScriptInvocationResult httpScriptInvocationResult, ScriptInvocationContext scriptInvocationContext)
        {
            ScriptInvocationResult scriptInvocationResult = new ScriptInvocationResult()
            {
                Outputs = new Dictionary<string, object>()
            };

            foreach (var outputBindingMetadata in scriptInvocationContext.FunctionMetadata.OutputBindings)
            {
                object outputValue = GetOutputValue(outputBindingMetadata.Name, outputBindingMetadata.Type, outputBindingMetadata.DataType, httpScriptInvocationResult.Outputs);
                if (outputValue != null)
                {
                    scriptInvocationResult.Outputs[outputBindingMetadata.Name] = outputValue;
                }
            }

            if (httpScriptInvocationResult.ReturnValue != null)
            {
                BindingMetadata returnParameterBindingMetadata = GetBindingMetadata(ScriptConstants.SystemReturnParameterBindingName, scriptInvocationContext);
                if (returnParameterBindingMetadata != null)
                {
                    scriptInvocationResult.Return = GetBindingValue(returnParameterBindingMetadata.DataType, httpScriptInvocationResult.ReturnValue);
                }
                else
                {
                    // Some triggers (like Durable Functions' triggers) assign $return implicitly.
                    scriptInvocationResult.Return = httpScriptInvocationResult.ReturnValue;
                }
            }
            return scriptInvocationResult;
        }

        internal static object GetOutputValue(string outputBindingName, string bindingType, DataType? bindingDataType, IDictionary<string, object> outputsFromWorker)
        {
            if (outputsFromWorker == null)
            {
                return null;
            }
            object outputBindingValue;
            if (bindingType == "http" && !outputBindingName.Equals(ScriptConstants.SystemReturnParameterBindingName))
            {
                return GetHttpOutputBindingResponse(outputBindingName, outputsFromWorker);
            }
            if (outputsFromWorker.TryGetValue(outputBindingName, out outputBindingValue))
            {
                return GetBindingValue(bindingDataType, outputBindingValue);
            }
            return null;
        }

        private static object GetBindingValue(DataType? bindingDataType, object outputBindingValue)
        {
            if (bindingDataType == DataType.Binary)
            {
                return outputBindingValue;
            }
            else
            {
                try
                {
                    return Convert.FromBase64String((string)outputBindingValue);
                }
                catch
                {
                    //ignore
                }
            }
            return outputBindingValue;
        }

        internal static object GetHttpOutputBindingResponse(string bindingName, IDictionary<string, object> outputsFromWorker)
        {
            dynamic httpOutput = new ExpandoObject();

            if (outputsFromWorker.TryGetValue(bindingName, out object outputBindingValue))
            {
                try
                {
                    HttpOutputBindingResponse httpOutputBindingResponse = JsonConvert.DeserializeObject<HttpOutputBindingResponse>(outputBindingValue.ToString());

                    httpOutput.StatusCode = httpOutputBindingResponse.StatusCode;
                    httpOutput.Status = httpOutputBindingResponse.Status;
                    httpOutput.Body = httpOutputBindingResponse.Body;
                    httpOutput.Headers = httpOutputBindingResponse.Headers;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Failed while trying to Deserialize to httpOutputBindingResponse. Output is not in expected format", ex.InnerException);
                }
            }
            return httpOutput;
        }

        private static BindingMetadata GetBindingMetadata(string outputBidingName, ScriptInvocationContext scriptInvocationContext)
        {
            // Find BindingMetadata that matches output form http response
            return scriptInvocationContext.FunctionMetadata.OutputBindings.FirstOrDefault(outputBindingMetadata => outputBindingMetadata.Name == outputBidingName);
        }
    }
}
