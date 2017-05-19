// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Config;
using HttpHandler = Microsoft.Azure.WebJobs.IAsyncConverter<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage>;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    // Gives binding extensions access to a http handler.
    // This is registered with the JobHostConfiguration and extensions will call on it to register for a handler.
    internal class WebJobsSdkExtensionHookProvider : IWebHookProvider
    {
        // Map from an extension name to a http handler.
        private IDictionary<string, HttpHandler> _customHttpHandlers = new Dictionary<string, HttpHandler>(StringComparer.OrdinalIgnoreCase);

        // Get a registered handler, or null
        public HttpHandler GetHandlerOrNull(string name)
        {
            HttpHandler handler;
            _customHttpHandlers.TryGetValue(name, out handler);
            return handler;
        }

        // Exposed to extensions to get get the URL for their http handler.
        public Uri GetUrl(IExtensionConfigProvider extension)
        {
            var extensionType = extension.GetType();
            var handler = extension as HttpHandler;
            if (handler == null)
            {
                throw new InvalidOperationException($"Extension must implemnent IAsyncConverter<HttpRequestMessage, HttpResponseMessage> in order to receive hooks");
            }

            string name = extensionType.Name;
            _customHttpHandlers[name] = handler;

            return GetExtensionWebHookRoute(name);
        }

        // Provides the URL for accessing the admin extensions WebHook route.
        internal static Uri GetExtensionWebHookRoute(string name)
        {
            var settings = ScriptSettingsManager.Instance;
            var hostName = settings.GetSetting(EnvironmentSettingNames.AzureWebsiteHostName);
            if (hostName == null)
            {
                return null;
            }

            bool isLocalhost = hostName.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase);
            var scheme = isLocalhost ? "http" : "https";

            return new Uri($"{scheme}://{hostName}/admin/extensions/{name}");
        }
    }
}