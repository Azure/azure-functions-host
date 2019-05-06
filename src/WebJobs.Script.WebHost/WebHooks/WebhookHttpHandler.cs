// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using Microsoft.Azure.WebJobs.Host.Config;
using CoreHttpHandler = Microsoft.Azure.WebJobs.IAsyncConverter<Microsoft.AspNetCore.Http.HttpRequest, Microsoft.AspNetCore.Mvc.IActionResult>;
using LegacyHttpHandler = Microsoft.Azure.WebJobs.IAsyncConverter<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage>;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebhookHttpHandler : CoreHttpHandler
    {
        private readonly CoreHttpHandler _coreHttpHandler;
        private readonly LegacyHttpHandler _legacyHttpHandler;

        public WebhookHttpHandler(IExtensionConfigProvider extensionConfig)
        {
            _coreHttpHandler = extensionConfig as CoreHttpHandler;
            _legacyHttpHandler = extensionConfig as LegacyHttpHandler;
            if (_coreHttpHandler == null && _legacyHttpHandler == null)
            {
                throw new ArgumentException($"Extension must implement IAsyncConverter<HttpRequestMessage, HttpResponseMessage> or IAsyncConverter<HttpRequest, IActionResult> in order to receive webhooks");
            }
        }

        public static WebhookHttpHandler GetHandlerFromExtension(IExtensionConfigProvider extensionConfig)
        {
            var handler = new WebhookHttpHandler(extensionConfig);
            if (handler._coreHttpHandler == null && handler._legacyHttpHandler == null)
            {
                return null;
            }
            return handler;
        }

        public async Task<IActionResult> ConvertAsync(HttpRequest input, CancellationToken cancellationToken)
        {
            if (_coreHttpHandler != null)
            {
                return await _coreHttpHandler.ConvertAsync(input, cancellationToken);
            }
            else if (_legacyHttpHandler != null)
            {
                var requestMessage = new HttpRequestMessageFeature(input.HttpContext).HttpRequestMessage;
                HttpResponseMessage response = await _legacyHttpHandler.ConvertAsync(requestMessage, cancellationToken);

                var result = new ObjectResult(response);
                result.Formatters.Add(new HttpResponseMessageOutputFormatter());
                return result;
            }
            else
            {
                // Should never get to this else statement
                throw new InvalidOperationException($"Extension must implement IAsyncConverter<HttpRequestMessage, HttpResponseMessage> or IAsyncConverter<HttpRequest, IActionResult> in order to receive webhooks");
            }
        }
    }
}