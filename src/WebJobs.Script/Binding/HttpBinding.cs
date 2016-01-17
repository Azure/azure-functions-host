// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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

            // read the content as a JObject
            JObject jsonObject = null;
            using (StreamReader streamReader = new StreamReader(context.Value))
            {
                string content = await streamReader.ReadToEndAsync();
                jsonObject = JObject.Parse(content);
            }

            HttpResponseMessage response = new HttpResponseMessage();
            response.StatusCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), (string)jsonObject["status"]);
            response.Content = new StringContent((string)jsonObject["body"]);

            request.Properties[HttpResponsePropertyKey] = response;
        }
    }
}
