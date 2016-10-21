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

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            return null;
        }

        public override async Task BindAsync(BindingContext context)
        {
            HttpRequestMessage request = (HttpRequestMessage)context.TriggerValue;

            object content = context.Value;
            if (content is Stream)
            {
                using (StreamReader streamReader = new StreamReader((Stream)content))
                {
                    content = await streamReader.ReadToEndAsync();
                }
            }

            HttpStatusCode statusCode = HttpStatusCode.OK;
            JObject headers = null;
            if (content is string)
            {
                try
                {
                    // attempt to read the content as a JObject
                    JObject jo = JObject.Parse((string)content);

                    // if the content is json we capture that so it will be
                    // serialized as json by WebApi below
                    content = jo;

                    // TODO: Improve this logic
                    // Sniff the object to see if it looks like a response object
                    // by convention
                    JToken value = null;
                    if (jo.TryGetValue("body", StringComparison.OrdinalIgnoreCase, out value))
                    {
                        content = value;

                        if (value is JValue && ((JValue)value).Type == JTokenType.String)
                        {
                            // convert raw strings so they get serialized properly below
                            content = (string)value;
                        }

                        if (jo.TryGetValue("headers", StringComparison.OrdinalIgnoreCase, out value) && value is JObject)
                        {
                            headers = (JObject)value;
                        }

                        if (jo.TryGetValue("status", StringComparison.OrdinalIgnoreCase, out value) && value is JValue)
                        {
                            statusCode = (HttpStatusCode)(int)value;
                        } 
                    }
                }
                catch (JsonException)
                {
                    // not a json response
                }
            }

            HttpResponseMessage response = null;
            if (content is string)
            {
                // for raw strings, we compose the content ourselves, otherwise WebApi
                // will serialize it as JSON and add quotes/double quotes to the string
                response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent((string)content)
                };
            }
            else
            {
                // let WebApi do its default serialization and content negotiation
                response = request.CreateResponse(statusCode, content);
            }

            if (headers != null)
            {
                // apply any user specified headers
                foreach (var header in headers)
                {
                    AddResponseHeader(response, header);
                }
            }

            request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
        }
        
        public void ProcessResult(IDictionary<string, object> functionArguments, object[] systemArguments, string triggerInputName, object result)
        {
            if (result == null)
            {
                return;
            }

            HttpRequestMessage request = (HttpRequestMessage)functionArguments.Values.Union(systemArguments).FirstOrDefault(p => p is HttpRequestMessage);
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
                        MediaTypeHeaderValue mediaType = null;
                        if (MediaTypeHeaderValue.TryParse(header.Value.ToString(), out mediaType))
                        {
                            response.Content.Headers.ContentType = mediaType;
                        }
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
