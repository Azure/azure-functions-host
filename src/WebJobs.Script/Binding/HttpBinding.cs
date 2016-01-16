// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class HttpBinding : Binding
    {
        internal const string HttpResponsePropertyKey = "HttpResponse";

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

            string content;
            using (StreamReader streamReader = new StreamReader(context.Value))
            {
                content = await streamReader.ReadToEndAsync();
            }

            HttpResponseMessage response;
            try
            {
                // attempt to read the content as a JObject
                JObject jsonObject = JObject.Parse(content);
                response = new HttpResponseMessage();
                response.StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), (string)jsonObject["status"]);
                response.Content = new StringContent((string)jsonObject["body"]);
            }
            catch (JsonException)
            {
                // if not json, then send the raw content as the body
                response = new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(content)
                };
            }

            request.Properties[HttpResponsePropertyKey] = response;
        }
    }
}
