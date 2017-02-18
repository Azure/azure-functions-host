// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Globalization;
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

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class HttpBinding : FunctionBinding, IResultProcessingBinding
    {
        private readonly bool _isProxy;

        public HttpBinding(ScriptHostConfiguration config, BindingMetadata metadata, FileAccess access) :
            base(config, metadata, access)
        {
            _isProxy = metadata.IsProxy;
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            return null;
        }

        public override Task BindAsync(BindingContext context)
        {
            HttpRequestMessage request = null;
            HttpResponseMessage response = null;

            if(context.TriggerValue is HttpRequestMessage)
            {
                request = (HttpRequestMessage)context.TriggerValue;
            }
            else if(context.TriggerValue is HttpResponseMessage)
            {
                response = (HttpResponseMessage)context.TriggerValue;
            }

            object content = context.Value;
            if (content is Stream)
            {
                // for script language functions (e.g. PowerShell, BAT, etc.) the value
                // will be a Stream which we need to convert to string
                ConvertStreamToValue((Stream)content, DataType.String, ref content);
            }

            if (_isProxy)
            {
                if (content is ExpandoObject)
                {
                    if (request != null)
                    {
                        HttpRequestMessage newRequest = CreateProxyRequestObject(content);
                        CreateResponseForProxies(newRequest, ref request, ref response);
                    }
                    else
                    {
                        HttpResponseMessage newResponse = CreateProxyResponseObject(content);
                        CreateResponseForProxies(newResponse, ref request, ref response);
                        request = (HttpRequestMessage)context.Binder.BindingData["res"];
                    }
                }
            }
            else
            {
                response = CreateResponse(request, content);
            }

            request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = response;

            return Task.CompletedTask;
        }

        internal static HttpResponseMessage CreateResponse(HttpRequestMessage request, object content)
        {
            string stringContent = content as string;
            if (stringContent != null)
            {
                try
                {
                    // attempt to read the content as JObject/JArray
                    content = JsonConvert.DeserializeObject(stringContent);
                }
                catch (JsonException)
                {
                    // not a json response
                }
            }

            // see if the content is a response object, defining http response properties
            IDictionary<string, object> responseObject = null;
            if (content is JObject)
            {
                responseObject = JsonConvert.DeserializeObject<ExpandoObject>(stringContent);
            }
            else
            {
                // Handle ExpandoObjects
                responseObject = content as ExpandoObject;
            }

            HttpStatusCode statusCode = HttpStatusCode.OK;
            IDictionary<string, object> responseHeaders = null;
            bool isRawResponse = false;
            if (responseObject != null)
            {
                ParseResponseObject(responseObject, ref content, out responseHeaders, out statusCode, out isRawResponse);
            }

            HttpResponseMessage response = CreateResponse(request, statusCode, content, responseHeaders, isRawResponse);

            if (responseHeaders != null)
            {
                // apply any user specified headers
                foreach (var header in responseHeaders)
                {
                    AddResponseHeader(response, header);
                }
            }

            return response;
        }

        internal static void ParseResponseObject(IDictionary<string, object> responseObject, ref object content, out IDictionary<string, object> headers, out HttpStatusCode statusCode, out bool isRawResponse)
        {
            headers = null;
            statusCode = HttpStatusCode.OK;
            isRawResponse = false;

            // TODO: Improve this logic
            // Sniff the object to see if it looks like a response object
            // by convention
            object bodyValue = null;
            if (responseObject.TryGetValue("body", out bodyValue, ignoreCase: true))
            {
                // the response content becomes the specified body value
                content = bodyValue;

                IDictionary<string, object> headersValue = null;
                if (responseObject.TryGetValue<IDictionary<string, object>>("headers", out headersValue, ignoreCase: true))
                {
                    headers = headersValue;
                }

                HttpStatusCode responseStatusCode;
                if (TryParseStatusCode(responseObject, out responseStatusCode))
                {
                    statusCode = responseStatusCode;
                }

                bool isRawValue;
                if (responseObject.TryGetValue<bool>("isRaw", out isRawValue, ignoreCase: true))
                {
                    isRawResponse = isRawValue;
                }
            }
        }

        internal static bool TryParseStatusCode(IDictionary<string, object> responseObject, out HttpStatusCode statusCode)
        {
            statusCode = HttpStatusCode.OK;
            object statusValue;

            if (!responseObject.TryGetValue("statusCode", out statusValue, ignoreCase: true) &&
                !responseObject.TryGetValue("status", out statusValue, ignoreCase: true))
            {
                return false;
            }

            if (statusValue is HttpStatusCode ||
                statusValue is int)
            {
                statusCode = (HttpStatusCode)statusValue;
                return true;
            }

            if (statusValue is uint ||
                statusValue is short ||
                statusValue is ushort ||
                statusValue is long ||
                statusValue is ulong)
            {
                statusCode = (HttpStatusCode)Convert.ToInt32(statusValue);
                return true;
            }

            var stringValue = statusValue as string;
            int parsedStatusCode;
            if (stringValue != null && int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedStatusCode))
            {
                statusCode = (HttpStatusCode)parsedStatusCode;
                return true;
            }

            return false;
        }

        private static HttpResponseMessage CreateResponse(HttpRequestMessage request, HttpStatusCode statusCode, object content, IDictionary<string, object> headers, bool isRawResponse)
        {
            if (isRawResponse)
            {
                // We only write the response through one of the formatters if
                // the function hasn't indicated that it wants to write the raw response
                return new HttpResponseMessage(statusCode) { Content = CreateResultContent(content) };
            }

            string contentType = null;
            MediaTypeHeaderValue mediaType = null;
            if (content != null &&
                (headers?.TryGetValue<string>("content-type", out contentType, ignoreCase: true) ?? false) &&
                MediaTypeHeaderValue.TryParse((string)contentType, out mediaType))
            {
                var writer = request.GetConfiguration().Formatters.FindWriter(content.GetType(), mediaType);
                if (writer != null)
                {
                    return new HttpResponseMessage(statusCode)
                    {
                        Content = new ObjectContent(content.GetType(), content, writer, mediaType)
                    };
                }

                // create a non-negotiated result content
                HttpContent resultContent = CreateResultContent(content, mediaType.MediaType);
                return new HttpResponseMessage(statusCode)
                {
                    Content = resultContent
                };
            }

            return CreateNegotiatedResponse(request, statusCode, content);
        }

        internal static HttpContent CreateResultContent(object content, string mediaType = null)
        {
            if (content is byte[])
            {
                return new ByteArrayContent((byte[])content);
            }
            else if (content is Stream)
            {
                return new StreamContent((Stream)content);
            }

            string stringContent;
            if (content is ExpandoObject)
            {
                stringContent = Utility.ToJson((ExpandoObject)content, Formatting.None);
            }
            else
            {
                stringContent = content?.ToString() ?? string.Empty;
            }

            return new StringContent(stringContent, null, mediaType);
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

            // ObjectContent can handle ExpandoObjects as well
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
            HttpResponseMessage response = null;

            if (request != null)
            {
                if (_isProxy)
                {
                    CreateResponseForProxies(result, ref request, ref response);
                }
                else
                {
                    response = result as HttpResponseMessage;
                    if (response == null)
                    {
                        response = CreateNegotiatedResponse(request, HttpStatusCode.OK, result);
                    }
                }
                request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
            }
        }

        private static void CreateResponseForProxies(object result, ref HttpRequestMessage request, ref HttpResponseMessage response)
        {
            var requestObjectFromFunction = result as HttpRequestMessage;

            if (requestObjectFromFunction != null)
            {
                var requestValue = ProxyHttpExtensions.Serialize(requestObjectFromFunction);

                response = new HttpResponseMessage();

                response.StatusCode = HttpStatusCode.OK;
                response.RequestMessage = requestObjectFromFunction;
                response.Content = new StringContent(requestValue);

                // putting back the original request to the function in the pipeline.
                request = (HttpRequestMessage)request.Properties[ScriptConstants.AzureFunctionsHttpProxyRoutingDataKey];
            }
            else
            {
                var responseObjectFromFunction = result as HttpResponseMessage;
                if (responseObjectFromFunction != null)
                {
                    var responseValue = ProxyHttpExtensions.Serialize(responseObjectFromFunction);

                    response = new HttpResponseMessage();

                    response.StatusCode = HttpStatusCode.OK;
                    response.Content = new StringContent(responseValue);
                }
            }
        }

        private static HttpRequestMessage CreateProxyRequestObject(object content)
        {
            IDictionary<string, object> requestObject = (ExpandoObject)content;
            var newRequest = new HttpRequestMessage();

            object method = null;
            if (requestObject.TryGetValue("method", out method, ignoreCase: true))
            {
                newRequest.Method = new HttpMethod(method.ToString());
            }

            object uri = null;
            if (requestObject.TryGetValue("originalUrl", out uri, ignoreCase: true))
            {
                newRequest.RequestUri = new Uri(uri.ToString());
            }

            IDictionary<string, object> headersValue = null;
            if (requestObject.TryGetValue<IDictionary<string, object>>("headers", out headersValue, ignoreCase: true))
            {
                foreach (var header in headersValue)
                {
                    newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
                }
            }

            JObject jsonContent = new JObject();
            CreateProxyContent(requestObject, jsonContent);

            object bodyValue = null;
            if (requestObject.TryGetValue("body", out bodyValue, ignoreCase: true))
            {
                jsonContent.Add("Content", JToken.FromObject(bodyValue));
            }

            ProxyHttpExtensions.DeserializeContent(newRequest, jsonContent);
            return newRequest;
        }

        private static HttpResponseMessage CreateProxyResponseObject(object content)
        {
            IDictionary<string, object> responseObject = (ExpandoObject)content;
            var newResponse = new HttpResponseMessage();

            object status = null;
            if (responseObject.TryGetValue("statusCode", out status, ignoreCase: true))
            {
                HttpStatusCode statusCode;
                if (Enum.TryParse(status.ToString(), out statusCode))
                {
                    newResponse.StatusCode = statusCode;
                }
                else
                {
                    newResponse.StatusCode = HttpStatusCode.InternalServerError;
                }
            }

            object reasonPhrase = null;
            if (responseObject.TryGetValue("ReasonPhrase", out reasonPhrase, ignoreCase: true))
            {
                newResponse.ReasonPhrase = reasonPhrase.ToString();
            }

            IDictionary<string, object> headersValue = null;
            if (responseObject.TryGetValue<IDictionary<string, object>>("headers", out headersValue, ignoreCase: true))
            {
                foreach (var header in headersValue)
                {
                    newResponse.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
                }
            }

            JObject jsonContent = new JObject();
            CreateProxyContent(responseObject, jsonContent);

            object bodyValue = null;
            if (responseObject.TryGetValue("responseBody", out bodyValue, ignoreCase: true))
            {
                jsonContent.Add("Content", JToken.FromObject(bodyValue));
            }

            ProxyHttpExtensions.DeserializeContent(newResponse, jsonContent);

            object requestInResponse = null;
            if (responseObject.TryGetValue("request", out requestInResponse, ignoreCase: true))
            {
                newResponse.RequestMessage = CreateProxyRequestObject(requestInResponse);
            }

            return newResponse;
        }

        private static void CreateProxyContent(IDictionary<string, object> responseObject, JObject jsonContent)
        {
            IDictionary<string, object> contentHeadersValue = null;
            if (responseObject.TryGetValue<IDictionary<string, object>>("contentHeaders", out contentHeadersValue, ignoreCase: true))
            {
                JArray jsonArray = new JArray();
                foreach (var contentHeader in contentHeadersValue)
                {
                    JObject jsonHeader = new JObject();
                    jsonHeader.Add("Key", contentHeader.Key);
                    jsonHeader.Add("Value", new JArray(contentHeader.Value.ToString()));
                    jsonArray.Add(jsonHeader);
                }

                if (jsonArray.Count > 0)
                {
                    jsonContent.Add("Headers", jsonArray);
                }
            }
        }

        public bool CanProcessResult(object result)
        {
            return result != null;
        }

        internal static void AddResponseHeader(HttpResponseMessage response, KeyValuePair<string, object> header)
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
                        if (response.Content != null && MediaTypeHeaderValue.TryParse(header.Value.ToString(), out mediaType))
                        {
                            response.Content.Headers.ContentType = mediaType;
                        }
                        break;
                    case "content-length":
                        long contentLength;
                        if (response.Content != null && long.TryParse(header.Value.ToString(), out contentLength))
                        {
                            response.Content.Headers.ContentLength = contentLength;
                        }
                        break;
                    case "content-disposition":
                        ContentDispositionHeaderValue contentDisposition = null;
                        if (response.Content != null && ContentDispositionHeaderValue.TryParse(header.Value.ToString(), out contentDisposition))
                        {
                            response.Content.Headers.ContentDisposition = contentDisposition;
                        }
                        break;
                    case "content-encoding":
                    case "content-language":
                    case "content-range":
                        if (response.Content != null)
                        {
                            response.Content.Headers.Add(header.Key, header.Value.ToString());
                        }
                        break;
                    case "content-location":
                        Uri uri;
                        if (response.Content != null && Uri.TryCreate(header.Value.ToString(), UriKind.Absolute, out uri))
                        {
                            response.Content.Headers.ContentLocation = uri;
                        }
                        break;
                    case "content-md5":
                        byte[] value;
                        if (response.Content != null)
                        {
                            if (header.Value is string)
                            {
                                value = Convert.FromBase64String((string)header.Value);
                            }
                            else
                            {
                                value = header.Value as byte[];
                            }
                            response.Content.Headers.ContentMD5 = value;
                        }
                        break;
                    case "expires":
                        if (response.Content != null && DateTimeOffset.TryParse(header.Value.ToString(), out dateTimeOffset))
                        {
                            response.Content.Headers.Expires = dateTimeOffset;
                        }
                        break;
                    case "last-modified":
                        if (response.Content != null && DateTimeOffset.TryParse(header.Value.ToString(), out dateTimeOffset))
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
