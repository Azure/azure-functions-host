// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class HttpBinding : FunctionBinding
    {
        internal const string HttpResponsePropertyKey = "MS_AzureFunctionsHttpResponse";

        public HttpBinding(ScriptHostConfiguration config, string name, FileAccess fileAccess, bool isTrigger) : base(config, name, "http", fileAccess, isTrigger)
        {
        }

        public override bool HasBindingParameters
        {
            get
            {
                return false;
            }
        }

        public override async Task BindAsync(BindingContext context)
        {
            HttpRequestMessage request = (HttpRequestMessage)context.Input;

            HttpResponseMessage response = context.Value as HttpResponseMessage;
            if (response == null)
            {
                // TODO: Need to handle Stream conversions properly
                Stream valueStream = context.Value as Stream;
                string content;
                using (StreamReader streamReader = new StreamReader(valueStream))
                {
                    content = await streamReader.ReadToEndAsync();
                }

                try
                {
                    // attempt to read the content as a JObject
                    JObject jsonObject = JObject.Parse(content);

                    // TODO: This logic needs to be made more robust
                    // E.g. we might decide to use a Regex to determine if
                    // the json is a response body or not
                    if (jsonObject["status"] != null && jsonObject["body"] != null)
                    {
                        HttpStatusCode statusCode = (HttpStatusCode)jsonObject.Value<int>("status");
                        string body = jsonObject.Value<string>("body");

                        response = new HttpResponseMessage(statusCode);
                        response.Content = new StringContent(body);
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
            }

            request.Properties[HttpResponsePropertyKey] = response;
        }
    }
}
