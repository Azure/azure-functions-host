// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Config;
using HttpHandler = Microsoft.Azure.WebJobs.IAsyncConverter<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage>;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    // Gives binding extensions access to a http handler.
    // This is registered with the JobHostConfiguration and extensions will call on it to register for a handler.
    internal class WebJobsSdkExtensionHookProvider : IWebHookProvider
    {
        private readonly ISecretManager _secretManager;

        // Map from an extension name to a http handler.
        private IDictionary<string, HttpHandler> _customHttpHandlers = new Dictionary<string, HttpHandler>(StringComparer.OrdinalIgnoreCase);

        public WebJobsSdkExtensionHookProvider(ISecretManager secretManager)
        {
            _secretManager = secretManager;
        }

        // Get a registered handler, or null
        public HttpHandler GetHandlerOrNull(string name)
        {
            HttpHandler handler;
            _customHttpHandlers.TryGetValue(name, out handler);

            return handler;
        }

        // Exposed to extensions to get the URL for their http handler.
        public Uri GetUrl(IExtensionConfigProvider extension)
        {
            var extensionType = extension.GetType();
            var handler = extension as HttpHandler;
            if (handler == null)
            {
                throw new InvalidOperationException($"Extension must implement IAsyncConverter<HttpRequestMessage, HttpResponseMessage> in order to receive webhooks");
            }

            string name = extensionType.Name;
            _customHttpHandlers[name] = handler;

            return GetExtensionWebHookRoute(name);
        }

        // Provides the URL for accessing the admin extensions WebHook route.
        private Uri GetExtensionWebHookRoute(string extensionName)
        {
            var settings = ScriptSettingsManager.Instance;
            var hostName = settings.GetSetting(EnvironmentSettingNames.AzureWebsiteHostName);
            if (hostName == null)
            {
                return null;
            }

            bool isLocalhost = hostName.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase);
            var scheme = isLocalhost ? "http" : "https";
            string keyValue = GetOrCreateExtensionKey(extensionName).GetAwaiter().GetResult();

            return new Uri($"{scheme}://{hostName}/admin/extensions/{extensionName}?code={keyValue}");
        }

        private async Task<string> GetOrCreateExtensionKey(string extensionName)
        {
            var hostSecrets = _secretManager.GetHostSecretsAsync().GetAwaiter().GetResult();
            string keyName = GetKeyName(extensionName);
            string keyValue = null;
            if (!hostSecrets.SystemKeys.TryGetValue(keyName, out keyValue))
            {
                // if the requested secret doesn't exist, create it on demand
                keyValue = SecretManager.GenerateSecret();
                await _secretManager.AddOrUpdateFunctionSecretAsync(keyName, keyValue, HostKeyScopes.SystemKeys, ScriptSecretsType.Host);
            }

            return keyValue;
        }

        internal static string GetKeyName(string extensionName)
        {
            // key names for extension webhooks are named by convention
            return $"{extensionName}_extension".ToLowerInvariant();
        }
    }
}