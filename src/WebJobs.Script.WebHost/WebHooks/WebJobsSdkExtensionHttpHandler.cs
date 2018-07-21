// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Host.Config;
using CoreHttpHandler = Microsoft.Azure.WebJobs.IAsyncConverter<Microsoft.AspNetCore.Http.HttpContext, Microsoft.AspNetCore.Mvc.IActionResult>;
using LegacyHttpHandler = Microsoft.Azure.WebJobs.IAsyncConverter<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage>;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebJobsSdkExtensionHttpHandler : CoreHttpHandler, LegacyHttpHandler
    {
        private readonly CoreHttpHandler _coreHttpHandler;
        private readonly LegacyHttpHandler _legacyHttpHandler;

        public WebJobsSdkExtensionHttpHandler(IExtensionConfigProvider extensionConfig)
        {
            _coreHttpHandler = extensionConfig as CoreHttpHandler;
            _legacyHttpHandler = extensionConfig as LegacyHttpHandler;
            if (_coreHttpHandler == null && _legacyHttpHandler == null)
            {
                throw new InvalidOperationException($"Extension must implement {typeof(CoreHttpHandler)} or {typeof(LegacyHttpHandler)} in order to receive webhooks");
            }
        }

        public WebJobsSdkExtensionHttpHandler(LegacyHttpHandler httpHandler)
        {
            _legacyHttpHandler = httpHandler ?? throw new ArgumentNullException(nameof(httpHandler));
        }

        public bool IsLegacy => _legacyHttpHandler != null;

        public async Task<IActionResult> ConvertAsync(HttpContext input, CancellationToken cancellationToken)
        {
            if (_coreHttpHandler == null)
            {
                throw new InvalidOperationException($"Cannot convert type of {typeof(HttpContext)} to {typeof(IActionResult)} using Legacy HTTP handler.");
            }
            return await _coreHttpHandler.ConvertAsync(input, cancellationToken);
        }

        public async Task<HttpResponseMessage> ConvertAsync(HttpRequestMessage input, CancellationToken cancellationToken)
        {
            if (_legacyHttpHandler == null)
            {
                throw new InvalidOperationException($"Cannot convert type of {typeof(HttpRequestMessage)} to {typeof(HttpResponseMessage)} using .NET Core HTTP handler");
            }
            return await _legacyHttpHandler.ConvertAsync(input, cancellationToken);
        }
    }
}
