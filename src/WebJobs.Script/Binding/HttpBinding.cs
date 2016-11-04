// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Reflection.Emit;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

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

            if (content is string)
            {
                try
                {
                    // attempt to read the content as a ExpandoObject
                    content = JsonConvert.DeserializeObject<ExpandoObject>((string)content, new ExpandoObjectConverter());
                }
                catch (JsonException)
                {
                    // not a json response
                }
            }
            
            IDictionary<string, object> headers = null;
            ExpandoObject responseObject = content as ExpandoObject;
            if (responseObject != null)
            {
                IDictionary<string, object> cleanResponse = new ExpandoObject();

                content = cleanResponse;
                foreach (var pair in responseObject)
                {
                    // strip functions
                    if (!(pair.Value is Delegate))
                    {
                        var key = pair.Key;
                        switch(pair.Key.ToLowerInvariant())
                        {
                            case "body":
                                // if there is a body, use it as content instead of cleanResponse
                                content = pair.Value;
                                break;

                            case "headers":
                                headers = pair.Value as IDictionary<string, object>;
                                break;

                            case "status":
                            case "statuscode":
                                statusCode = (HttpStatusCode)Convert.ToInt32(pair.Value);
                                break;
                        }

                        // rebuild the responseObject without functions
                        cleanResponse.Add(pair);
                    }
                }
            }

            // convert headers to lowercase (keeping this scoped so that no polluting lowercaseHeaders name)
            {
                IDictionary<string, object> lowercaseHeaders = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                headers?.ToList().ForEach(p => lowercaseHeaders[p.Key] = p.Value);
                headers = lowercaseHeaders;
            }

            HttpResponseMessage response = CreateResponse(request, statusCode, content, headers);

            // apply any user specified headers
            foreach (var header in headers)
            {
                AddResponseHeader(response, header);
            }

            request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
        }

        private static HttpResponseMessage CreateResponse(HttpRequestMessage request, HttpStatusCode statusCode, object content, IDictionary<string, object> headers)
        {
            object contentType = null;
            MediaTypeHeaderValue mediaType = null;
            if (content != null && headers.TryGetValue("content-type", out contentType) &&
                MediaTypeHeaderValue.TryParse(content as string, out mediaType))
            {
                MediaTypeFormatter writer = request.GetConfiguration()
                    .Formatters.FindWriter(content.GetType(), mediaType);

                if (writer != null)
                {
                    return new HttpResponseMessage(statusCode)
                    {
                        Content = new ObjectContent(content.GetType(), content, writer, mediaType)
                    };
                }

                HttpContent resultContent = CreateResultContent(content, mediaType.MediaType);

                if (resultContent != null)
                {
                    return new HttpResponseMessage(statusCode)
                    {
                        Content = resultContent
                    };
                }
            }

            return CreateNegotiatedResponse(request, statusCode, content);
        }

        private static HttpContent CreateResultContent(object content, string mediaType)
        {
            if (content is string)
            {
                return new StringContent((string)content, null, mediaType);
            }
            else if (content is byte[])
            {
                return new ByteArrayContent((byte[])content);
            }
            else if (content is Stream)
            {
                return new StreamContent((Stream)content);
            }

            return null;
        }

        private static HttpResponseMessage CreateNegotiatedResponse(HttpRequestMessage request, HttpStatusCode statusCode, object content)
        {
            HttpResponseMessage result = request.CreateResponse(statusCode);

            if (content == null)
            {
                return result;
            }

            var configuration = request.GetConfiguration();
            IContentNegotiator negotiator = configuration.Services.GetContentNegotiator();
            var negotiationResult = negotiator.Negotiate(content.GetType(), request, configuration.Formatters);

            result.Content = new ObjectContent(content.GetType(), content, negotiationResult.Formatter, negotiationResult.MediaType);

            return result;
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
                    response = CreateNegotiatedResponse(request, HttpStatusCode.OK, result);
                }

                request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
            }
        }

        public bool CanProcessResult(object result)
        {
            return result != null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private static void AddResponseHeader(HttpResponseMessage response, KeyValuePair<string, object> header)
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
                        if (header.Value is byte[])
                        {
                            response.Content.Headers.ContentMD5 = header.Value as byte[];
                        }
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
