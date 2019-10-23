// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc.Http
{
    public static class HttpScriptInvocationResultExtensions
    {
        public static ScriptInvocationResult ToScriptInvocationResult(this HttpScriptInvocationResult httpScriptInvocationResult, ScriptInvocationContext scriptInvocationContext)
        {
            ScriptInvocationResult scriptInvocationResult = new ScriptInvocationResult()
            {
                Outputs = new Dictionary<string, object>()
            };
            if (httpScriptInvocationResult.Outputs != null && httpScriptInvocationResult.Outputs.Any())
            {
                foreach (var outputFromHttpWorker in httpScriptInvocationResult.Outputs)
                {
                    BindingMetadata outputBindingMetadata = GetBindingMetadata(outputFromHttpWorker.Key, scriptInvocationContext);
                    scriptInvocationResult.Outputs[outputFromHttpWorker.Key] = GetOutputValue(outputBindingMetadata, outputFromHttpWorker.Value);
                }
            }
            if (httpScriptInvocationResult.ReturnValue != null)
            {
                BindingMetadata returnParameterBindingMetadata = GetBindingMetadata(ScriptConstants.SystemReturnParameterBindingName, scriptInvocationContext);
                scriptInvocationResult.Return = GetOutputValue(returnParameterBindingMetadata, httpScriptInvocationResult.ReturnValue);
            }
            return scriptInvocationResult;
        }

        private static object GetOutputValue(BindingMetadata bindingMetadata, object outputBindingValue)
        {
            if (bindingMetadata != null && outputBindingValue != null)
            {
                if (bindingMetadata.DataType == DataType.Binary)
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
            return null;
        }

        private static BindingMetadata GetBindingMetadata(string outputBidingName, ScriptInvocationContext scriptInvocationContext)
        {
            // Find dataType for outputbinding if exists
            return scriptInvocationContext.FunctionMetadata.OutputBindings.FirstOrDefault(outputBindingMetadata => outputBindingMetadata.Name == outputBidingName);
        }
    }
}
