// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class HttpBinding : FunctionBinding, IResultProcessingBinding
    {
        internal const string HttpResponsePropertyKey = "MS_AzureFunctionsHttpResponse";

        public HttpBinding(ScriptHostConfiguration config, string name, FileAccess access, bool isTrigger) : base(config, name, "http", access, isTrigger)
        {
        }

        public override bool HasBindingParameters
        {
            get
            {
                return false;
            }
        }

        public override CustomAttributeBuilder GetCustomAttribute()
        {
            return null;
        }

        public override async Task BindAsync(BindingContext context)
        {
            HttpRequestMessage request = (HttpRequestMessage)context.Input;

            string content;
            using (StreamReader streamReader = new StreamReader(context.Value))
            {
                content = await streamReader.ReadToEndAsync();
            }

            HttpResponseMessage response = null;
            try
            {
                // attempt to read the content as a JObject
                JObject jsonObject = JObject.Parse(content);

                // TODO: This logic needs to be made more robust
                // E.g. we might decide to use a Regex to determine if
                // the json is a response body or not
                if (jsonObject["body"] != null)
                {
                    HttpStatusCode statusCode = HttpStatusCode.OK;
                    if (jsonObject["status"] != null)
                    {
                        statusCode = (HttpStatusCode)jsonObject.Value<int>("status");
                    }

                    string body = jsonObject["body"].ToString();

                    response = new HttpResponseMessage(statusCode);
                    response.Content = new StringContent(body);

                    JObject headers = (JObject)jsonObject["headers"];
                    if (headers != null)
                    {
                        foreach (var header in headers)
                        {
                            if (header.Value != null)
                            {
                                response.Headers.Add(header.Key, header.Value.ToString());
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // not a json response
            }

            if (response == null)
            {
                // if unable to parse a json response just send
                // the raw content
                response = new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(content)
                };
            }

            request.Properties[HttpResponsePropertyKey] = response;
        }

        public void ProcessResult(object inputValue, object result)
        {
            HttpRequestMessage request = inputValue as HttpRequestMessage;

            if (request != null && result is HttpResponseMessage)
            {
                request.Properties[HttpResponsePropertyKey] = result;
            }
        }

        public bool CanProcessResult(object result)
        {
            return result is HttpResponseMessage;
        }
    }
}
