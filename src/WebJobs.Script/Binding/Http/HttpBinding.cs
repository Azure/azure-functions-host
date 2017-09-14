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
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class HttpBinding : FunctionBinding
    {
        public HttpBinding(ScriptHostConfiguration config, BindingMetadata metadata, FileAccess access)
            : base(config, metadata, access)
        {
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            return null;
        }

        public override Task BindAsync(BindingContext context)
        {
            HttpRequest request = (HttpRequest)context.TriggerValue;

            object content = context.Value;
            if (content is Stream)
            {
                // for script language functions (e.g. PowerShell, BAT, etc.) the value
                // will be a Stream which we need to convert to string
                ConvertStreamToValue((Stream)content, DataType.String, ref content);
            }

            IActionResult response = CreateResult(request, content);
            request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] = response;

            return Task.CompletedTask;
        }

        internal static IActionResult CreateResult(HttpRequest request, object content)
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
                // TODO: FACAVAL - The call bellow is pretty fragile. This would cause issues
                // if we invoke this with a JObject. Maintaining this to retain the original implementation
                // but this should be revisited.
                responseObject = JsonConvert.DeserializeObject<ExpandoObject>(content.ToString());
            }
            else
            {
                // Handle ExpandoObjects
                responseObject = content as ExpandoObject;
            }

            int statusCode = StatusCodes.Status200OK;
            IDictionary<string, object> responseHeaders = null;
            bool isRawResponse = false;
            if (responseObject != null)
            {
                ParseResponseObject(responseObject, ref content, out responseHeaders, out statusCode, out isRawResponse);
            }

            return CreateResult(request, statusCode, content, responseHeaders, isRawResponse);
        }

        internal static void ParseResponseObject(IDictionary<string, object> responseObject, ref object content, out IDictionary<string, object> headers, out int statusCode, out bool isRawResponse)
        {
            headers = null;
            statusCode = StatusCodes.Status200OK;
            isRawResponse = false;

            // TODO: Improve this logic
            // Sniff the object to see if it looks like a response object
            // by convention
            object bodyValue = null;
            if (responseObject.TryGetValue("body", out bodyValue, ignoreCase: true))
            {
                // the response content becomes the specified body value
                content = bodyValue;

                if (responseObject.TryGetValue("headers", out IDictionary<string, object> headersValue, ignoreCase: true))
                {
                    headers = headersValue;
                }

                if (TryParseStatusCode(responseObject, out int? responseStatusCode))
                {
                    statusCode = responseStatusCode.Value;
                }

                if (responseObject.TryGetValue<bool>("isRaw", out bool isRawValue, ignoreCase: true))
                {
                    isRawResponse = isRawValue;
                }
            }
        }

        internal static bool TryParseStatusCode(IDictionary<string, object> responseObject, out int? statusCode)
        {
            statusCode = StatusCodes.Status200OK;

            if (!responseObject.TryGetValue("statusCode", out object statusValue, ignoreCase: true) &&
                !responseObject.TryGetValue("status", out statusValue, ignoreCase: true))
            {
                return false;
            }

            if (statusValue is HttpStatusCode ||
                statusValue is int)
            {
                statusCode = (int)statusValue;
                return true;
            }

            if (statusValue is uint ||
                statusValue is short ||
                statusValue is ushort ||
                statusValue is long ||
                statusValue is ulong)
            {
                statusCode = Convert.ToInt32(statusValue);
                return true;
            }

            var stringValue = statusValue as string;
            int parsedStatusCode;
            if (stringValue != null && int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedStatusCode))
            {
                statusCode = parsedStatusCode;
                return true;
            }

            return false;
        }

        private static IActionResult CreateResult(HttpRequest request, int statusCode, object content, IDictionary<string, object> headers, bool isRawResponse)
        {
            if (isRawResponse)
            {
                // We only write the response through one of the formatters if
                // the function hasn't indicated that it wants to write the raw response
                return new RawScriptResult(statusCode, content) { Headers = headers };
            }

            var result = new ObjectResult(content)
            {
                StatusCode = statusCode
            };

            string contentType = null;
            if (content != null &&
                (headers?.TryGetValue("content-type", out contentType, ignoreCase: true) ?? false) &&
                MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue mediaType))
            {
                result.ContentTypes.Add(mediaType);
            }

            return result;
        }

        internal static void SetResponse(HttpRequest request, object result)
        {
            IActionResult actionResult = result as IActionResult;
            if (actionResult == null)
            {
                var objectResult = new ObjectResult(result);

                if (result is System.Net.Http.HttpResponseMessage)
                {
                    // To maintain backwards compatibility, if the type returned is an
                    // instance of an HttpResponseMessage, add the appropriate formatter to
                    // handle the response
                    objectResult.Formatters.Add(new HttpResponseMessageOutputFormatter());
                }

                actionResult = objectResult;
            }

            request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] = actionResult;
        }
    }
}