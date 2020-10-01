// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class HttpBinding : FunctionBinding
    {
        private static Lazy<bool> isActionResultHandlingEnabled = new Lazy<bool>(() => FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableActionResultHandling));

        public HttpBinding(ScriptJobHostOptions config, BindingMetadata metadata, FileAccess access)
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
            HttpResponseMessage invocationResponseMessage = context.Value as HttpResponseMessage;
            if (invocationResponseMessage != null)
            {
                request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] = invocationResponseMessage;
            }
            else
            {
                object content = context.Value;
                if (content is Stream)
                {
                    // for script language functions (e.g. PowerShell, BAT, etc.) the value
                    // will be a Stream which we need to convert to string
                    ConvertStreamToValue((Stream)content, DataType.String, ref content);
                }

                IActionResult response = CreateResult(request, content);
                request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
            }
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
            bool enableContentNegotiation = false;
            List<Tuple<string, string, CookieOptions>> cookies = null;
            if (responseObject != null)
            {
                ParseResponseObject(responseObject, ref content, out responseHeaders, out statusCode, out cookies, out enableContentNegotiation);
            }

            return CreateResult(request, statusCode, content, responseHeaders, cookies, enableContentNegotiation);
        }

        internal static void ParseResponseObject(IDictionary<string, object> responseObject, ref object content, out IDictionary<string, object> headers, out int statusCode, out List<Tuple<string, string, CookieOptions>> cookies, out bool enableContentNegotiation)
        {
            headers = null;
            cookies = null;
            statusCode = StatusCodes.Status200OK;
            enableContentNegotiation = false;

            // TODO: Improve this logic
            // Sniff the object to see if it looks like a response object
            // by convention
            object bodyValue = null;
            if (responseObject.TryGetValue(WorkerConstants.HttpBody, out bodyValue, ignoreCase: true))
            {
                // the response content becomes the specified body value
                content = bodyValue;

                if (responseObject.TryGetValue(WorkerConstants.HttpHeaders, out IDictionary<string, object> headersValue, ignoreCase: true))
                {
                    headers = headersValue;
                }

                if (TryParseStatusCode(responseObject, out int? responseStatusCode))
                {
                    statusCode = responseStatusCode.Value;
                }

                if (responseObject.TryGetValue<bool>(WorkerConstants.HttpEnableContentNegotiation, out bool enableContentNegotiationValue, ignoreCase: true))
                {
                    enableContentNegotiation = enableContentNegotiationValue;
                }

                if (responseObject.TryGetValue(WorkerConstants.HttpCookies, out List<Tuple<string, string, CookieOptions>> cookiesValue, ignoreCase: true))
                {
                    cookies = cookiesValue;
                }
            }
        }

        internal static bool TryParseStatusCode(IDictionary<string, object> responseObject, out int? statusCode)
        {
            statusCode = StatusCodes.Status200OK;

            if (!responseObject.TryGetValue(WorkerConstants.HttpStatusCode, out object statusValue, ignoreCase: true) &&
                !responseObject.TryGetValue(WorkerConstants.HttpStatus, out statusValue, ignoreCase: true))
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

        private static IActionResult CreateResult(HttpRequest request, int statusCode, object content, IDictionary<string, object> headers, List<Tuple<string, string, CookieOptions>> cookies, bool enableContentNegotiation)
        {
            if (enableContentNegotiation)
            {
                // We only write the response through one of the formatters if
                // the function has indicated that it wants to enable content negotiation
                return new ScriptObjectResult(content, headers) { StatusCode = statusCode };
            }
            else
            {
                return new RawScriptResult(statusCode, content)
                {
                    Headers = headers,
                    Cookies = cookies
                };
            }
        }

        private static IActionResult CreateObjectResult(object result)
        {
            var objectResult = new ObjectResult(result);

            if (result is HttpResponseMessage)
            {
                // To maintain backwards compatibility, if the type returned is an
                // instance of an HttpResponseMessage, add the appropriate formatter to
                // handle the response
                objectResult.Formatters.Add(new HttpResponseMessageOutputFormatter());
            }

            return objectResult;
        }

        internal static void SetResponse(HttpRequest request, object result)
        {
            // Use the existing response if already set (IBinder model).
            // This is always set by OOP languages.
            if (request.HttpContext.Items.TryGetValue(ScriptConstants.AzureFunctionsHttpResponseKey, out object existing) && existing is IActionResult)
            {
                return;
            }

            if (!(result is IActionResult actionResult))
            {
                if (result is IConvertToActionResult actionResultConvertible)
                {
                    actionResult = actionResultConvertible.Convert();
                }
                else
                {
                    actionResult = CreateObjectResult(result);
                }
            }

            request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] = actionResult;
        }

        /// <summary>
        /// This method is used when running in compatibility mode, maintained to preserve the behavior in 2.x
        /// </summary>
        internal static void LegacySetResponse(HttpRequest request, object result)
        {
            // use the existing response if already set (IBinder model)
            if (request.HttpContext.Items.TryGetValue(ScriptConstants.AzureFunctionsHttpResponseKey, out object existing) && existing is IActionResult)
            {
                return;
            }

            if (!(result is IActionResult actionResult))
            {
                if (result is Stream)
                {
                    // for script language functions (e.g. PowerShell, BAT, etc.) the value
                    // will be a Stream which we need to convert to string
                    FunctionBinding.ConvertStreamToValue((Stream)result, DataType.String, ref result);
                    actionResult = CreateResult(request, result);
                }
                else if (result is JObject)
                {
                    actionResult = CreateResult(request, result);
                }
                else if (isActionResultHandlingEnabled.Value && result is IConvertToActionResult actionResultConvertible)
                {
                    // Convert ActionResult<T> to ActionResult
                    actionResult = actionResultConvertible.Convert();
                }
                else
                {
                    actionResult = CreateObjectResult(result);
                }
            }

            request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] = actionResult;
        }
    }
}