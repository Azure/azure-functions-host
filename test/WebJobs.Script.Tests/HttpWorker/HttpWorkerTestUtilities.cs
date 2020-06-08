// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests.HttpWorker
{
    public class HttpWorkerTestUtilities
    {
        public const string QueryParamString = "?id=149656222&name=Ink%20And%20Toner";
        public const string UTF8AcceptCharset = "UTF-8";
        public const string AcceptHeaderValue = "text/plain";
        public const string HttpContentStringValue = "hello world";
        public const string HttpResponseContentStringValue = "hello world response";

        public static HttpRequest GetTestHttpRequest()
        {
            var httpRequest = new DefaultHttpContext().Request;
            httpRequest.Method = "GET";
            httpRequest.Query = GetTestQueryParams();
            httpRequest.Headers[HeaderNames.AcceptCharset] = UTF8AcceptCharset;
            httpRequest.Headers[HeaderNames.Accept] = AcceptHeaderValue;
            var json = JsonConvert.SerializeObject(HttpContentStringValue);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            httpRequest.Body = stream;
            httpRequest.ContentLength = stream.Length;
            return httpRequest;
        }

        public static QueryCollection GetTestQueryParams()
        {
            return new QueryCollection(QueryHelpers.ParseQuery(QueryParamString));
        }

        public static List<(string name, DataType type, object val)> GetSimpleHttpTriggerScriptInvocationInputs()
        {
            List<(string name, DataType type, object val)> inputs = new List<(string name, DataType type, object val)>();
            inputs.Add(("testInputReq", DataType.String, GetTestHttpRequest()));
            return inputs;
        }

        public static HttpScriptInvocationResult GetTestHttpScriptInvocationResult()
        {
            return new HttpScriptInvocationResult()
            {
                Logs = new List<string>() { "invocation log1", "invocation log2" },
                Outputs = new Dictionary<string, object>()
                {
                    { "output1", "output1Value" },
                    { "output2", "output2Value" }
                },
                ReturnValue = "Hello return"
            };
        }

        public static HttpScriptInvocationResult GetTestHttpScriptInvocationResult_DataType_Binary_Outputs()
        {
            // Response contains raw bytes
            byte[] outData1 = Encoding.UTF8.GetBytes("output1Value");
            byte[] outData2 = Encoding.UTF8.GetBytes("output2Value");
            byte[] retrunData = Encoding.UTF8.GetBytes("Hello return");
            return new HttpScriptInvocationResult()
            {
                Logs = new List<string>() { "invocation log1", "invocation log2" },
                Outputs = new Dictionary<string, object>()
                {
                    { "output1", outData1 },
                    { "output2", outData2 }
                },
                ReturnValue = retrunData
            };
        }

        public static HttpScriptInvocationResult GetTestHttpScriptInvocationResult_Binary_Outputs()
        {
            // Response contains base64 encoded strings
            byte[] outData1 = Encoding.UTF8.GetBytes("output1Value");
            byte[] outData2 = Encoding.UTF8.GetBytes("output2Value");
            byte[] retrunData = Encoding.UTF8.GetBytes("Hello return");
            return new HttpScriptInvocationResult()
            {
                Logs = new List<string>() { "invocation log1", "invocation log2" },
                Outputs = new Dictionary<string, object>()
                {
                    { "output1", Convert.ToBase64String(outData1) },
                    { "output2", Convert.ToBase64String(outData2) }
                },
                ReturnValue = Convert.ToBase64String(retrunData)
            };
        }

        public static HttpResponseMessage GetHttpResponseMessageWithJsonContent()
        {
            JObject response = new JObject();
            response["key1"] = "value1";
            response["key2"] = "value2";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToString(), System.Text.Encoding.UTF8, "application/json") };
        }

        public static Dictionary<string, object> GetScriptInvocationBindingData()
        {
            Dictionary<string, object> bindingData = new Dictionary<string, object>();
            bindingData["dequeueCount"] = 4;
            bindingData["VisibleTime"] = new DateTime(2019, 10, 1);
            bindingData["helloString"] = "helloMetadata";
            return bindingData;
        }

        public static HttpResponseMessage GetValidHttpResponseMessage()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ObjectContent<HttpScriptInvocationResult>(GetTestHttpScriptInvocationResult(), new JsonMediaTypeFormatter())
            };
        }

        public static HttpResponseMessage GetValidHttpResponseMessageWithJsonRes()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ObjectContent<HttpScriptInvocationResult>(GetHttpScriptInvocationResultWithJsonRes(), new JsonMediaTypeFormatter())
            };
        }

        public static HttpResponseMessage GetValidHttpResponseMessage_DataType_Binary_Data()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ObjectContent<HttpScriptInvocationResult>(GetTestHttpScriptInvocationResult_DataType_Binary_Outputs(), new JsonMediaTypeFormatter())
            };
        }

        public static HttpResponseMessage GetValidHttpResponseMessage_Binary_Data()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ObjectContent<HttpScriptInvocationResult>(GetTestHttpScriptInvocationResult_Binary_Outputs(), new JsonMediaTypeFormatter())
            };
        }

        public static HttpResponseMessage GetValidSimpleHttpResponseMessage()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(HttpResponseContentStringValue)
            };
        }

        public static HttpResponseMessage GetHttpResponseMessageWithStringContent()
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("TestMessage") };
        }

        public static List<(string name, DataType type, object val)> GetScriptInvocationInputs()
        {
            List<(string name, DataType type, object val)> inputs = new List<(string name, DataType type, object val)>();
            inputs.Add(("myqueueItem", DataType.String, "HelloWorld"));
            return inputs;
        }

        public static ScriptInvocationContext GetScriptInvocationContext(string functionName, Guid invocationId, ILogger testLogger, DataType dataType = DataType.String)
        {
            ScriptInvocationContext scriptInvocationContext = new ScriptInvocationContext()
            {
                ExecutionContext = new ExecutionContext()
                {
                    InvocationId = invocationId,
                    FunctionName = functionName,
                },
                BindingData = GetScriptInvocationBindingData(),
                Inputs = GetScriptInvocationInputs(),
                ResultSource = new TaskCompletionSource<ScriptInvocationResult>(),
                Logger = testLogger,
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture()
            };
            var functionMetadata = new FunctionMetadata
            {
                Name = functionName
            };

            var httpTriggerBinding = new BindingMetadata
            {
                Name = "req",
                Type = "httpTrigger",
                Direction = BindingDirection.In,
                Raw = new JObject()
            };

            var queueInputBinding = new BindingMetadata
            {
                Name = "queueInput",
                Type = "queue",
                Direction = BindingDirection.In,
                DataType = dataType
            };

            var httpOutputBinding = new BindingMetadata
            {
                Name = "res",
                Type = "http",
                Direction = BindingDirection.Out,
                Raw = new JObject(),
                DataType = dataType
            };

            var queueOutputBinding1 = new BindingMetadata
            {
                Name = "output1",
                Type = "queue",
                Direction = BindingDirection.Out,
                DataType = dataType
            };

            var queueOutputBinding2 = new BindingMetadata
            {
                Name = "output2",
                Type = "queue",
                Direction = BindingDirection.Out,
                DataType = dataType
            };

            var queueOutputRetrun = new BindingMetadata
            {
                Name = "$return",
                Type = "queue",
                Direction = BindingDirection.Out,
                DataType = dataType
            };
            functionMetadata.Bindings.Add(httpTriggerBinding);
            functionMetadata.Bindings.Add(queueOutputBinding1);
            functionMetadata.Bindings.Add(queueOutputBinding2);
            functionMetadata.Bindings.Add(queueOutputRetrun);
            scriptInvocationContext.FunctionMetadata = functionMetadata;
            return scriptInvocationContext;
        }

        public static HttpScriptInvocationResult GetHttpScriptInvocationResultWithJsonRes()
        {
            JObject httpRes = new JObject();
            httpRes["statusCode"] = "201";
            httpRes["body"] = "my world";
            return new HttpScriptInvocationResult()
            {
                Logs = new List<string>() { "invocation log1", "invocation log2" },
                Outputs = new Dictionary<string, object>()
                {
                    { "res", httpRes }
                }
            };
        }

        public static ScriptInvocationContext GetSimpleHttpTriggerScriptInvocationContext(string functionName, Guid invocationId, ILogger testLogger)
        {
            ScriptInvocationContext scriptInvocationContext = new ScriptInvocationContext()
            {
                ExecutionContext = new ExecutionContext()
                {
                    InvocationId = invocationId,
                    FunctionName = functionName,
                },
                BindingData = GetScriptInvocationBindingData(),
                Inputs = GetSimpleHttpTriggerScriptInvocationInputs(),
                ResultSource = new TaskCompletionSource<ScriptInvocationResult>(),
                Logger = testLogger,
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture()
            };
            var functionMetadata = new FunctionMetadata
            {
                Name = functionName
            };
            var httpTriggerBinding = new BindingMetadata
            {
                Name = "req",
                Type = "httpTrigger",
                Direction = BindingDirection.In,
                Raw = new JObject()
            };
            var httpOutputBinding = new BindingMetadata
            {
                Name = "res",
                Type = "http",
                Direction = BindingDirection.Out,
                Raw = new JObject()
            };
            functionMetadata.Bindings.Add(httpTriggerBinding);
            functionMetadata.Bindings.Add(httpOutputBinding);
            scriptInvocationContext.FunctionMetadata = functionMetadata;
            return scriptInvocationContext;
        }
    }
}
