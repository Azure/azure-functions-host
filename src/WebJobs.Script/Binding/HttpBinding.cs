// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class HttpBinding : FunctionBinding, IResultProcessingBinding
    {
        public HttpBinding(ScriptHostConfiguration config, BindingMetadata metadata, FileAccess access) : 
            base(config, metadata, access)
        {
        }

        public override bool HasBindingParameters
        {
            get
            {
                return false;
            }
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes()
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

                    // we default the Content-Type here, but we override below with any
                    // Content-Type header the user might have set themselves
                    // TODO: rather than newing up an HttpResponseMessage investigate using
                    // request.CreateResponse, which should allow WebApi Content negotiation to
                    // take place.
                    if (Utility.IsJson(body))
                    {
                        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    }

                    // apply any user specified headers
                    JObject headers = (JObject)jsonObject["headers"];
                    if (headers != null)
                    {
                        foreach (var header in headers)
                        {
                            AddResponseHeader(response, header);
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

            request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
        }
        
        public void ProcessResult(IDictionary<string, object> functionArguments, object[] systemArguments, string triggerInputName, object result)
        {
            if (result == null)
            {
                return;
            }

            HttpRequestMessage request;

            if (!functionArguments.TryGetValue(triggerInputName, out request))
            {
                // No argument is bound to the request message, so we should have 
                // it in the system arguments
                request = systemArguments.FirstOrDefault(a => a is HttpRequestMessage) as HttpRequestMessage;
            }

            if (request != null)
            {
                HttpResponseMessage response = result as HttpResponseMessage;
                if (response == null)
                {
                    response = request.CreateResponse(HttpStatusCode.OK);
                    response.Content = new ObjectContent(result.GetType(), result, new JsonMediaTypeFormatter());
                }

                request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
            }
        }

        public bool CanProcessResult(object result)
        {
            return result != null;
        }

        private static void AddResponseHeader(HttpResponseMessage response, KeyValuePair<string, JToken> header)
        {
            if (header.Value != null)
            {
                DateTimeOffset dateTimeOffset;
                switch (header.Key.ToLowerInvariant())
                {
                    // The following content headers must be added to the response
                    // content header collection
                    case "content-type":
                        response.Content.Headers.ContentType = new MediaTypeHeaderValue(header.Value.ToString());
                        break;
                    case "content-length":
                        long contentLength;
                        if (long.TryParse(header.Value.ToString(), out contentLength))
                        {
                            response.Content.Headers.ContentLength = contentLength;
                        }
                        break;
                    case "content-disposition":
                        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue(header.Value.ToString());
                        break;
                    case "content-encoding":
                    case "content-language":
                    case "content-range":
                        response.Content.Headers.Add(header.Key, header.Value.ToString());
                        break;
                    case "content-location":
                        Uri uri;
                        if (Uri.TryCreate(header.Value.ToString(), UriKind.Absolute, out uri))
                        {
                            response.Content.Headers.ContentLocation = uri;
                        }
                        break;
                    case "content-md5":
                        response.Content.Headers.ContentMD5 = header.Value.Value<byte[]>();
                        break;
                    case "expires":
                        if (DateTimeOffset.TryParse(header.Value.ToString(), out dateTimeOffset))
                        {
                            response.Content.Headers.Expires = dateTimeOffset;
                        }
                        break;
                    case "last-modified":
                        if (DateTimeOffset.TryParse(header.Value.ToString(), out dateTimeOffset))
                        {
                            response.Content.Headers.LastModified = dateTimeOffset;
                        }
                        break;
                    default:
                        // All other headers are added directly to the response
                        response.Headers.Add(header.Key, header.Value.ToString());
                        break;
                }
            }
        }
    }
}
