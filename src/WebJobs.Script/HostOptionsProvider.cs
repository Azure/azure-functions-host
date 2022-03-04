// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script
{
    public class HostOptionsProvider : IHostOptionsProvider
    {
        private readonly JsonSerializer _serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        });

        private readonly IEnumerable<IExtensionOptionsProvider> _extensionOptionsProviders;
        private readonly ILogger<HostOptionsProvider> _logger;
        private readonly IOptionsMonitor<ConcurrencyOptions> _concurrencyOptions;

        public HostOptionsProvider(IEnumerable<IExtensionOptionsProvider> extensionOptionsProviders, IOptionsMonitor<ConcurrencyOptions> concurrencyOptions, ILogger<HostOptionsProvider> logger)
        {
            _extensionOptionsProviders = extensionOptionsProviders;
            _logger = logger;
            _concurrencyOptions = concurrencyOptions;
        }

        public JObject GetOptions()
        {
            var payload = new JObject();
            var extensions = new JObject();
            var extensionOptions = GetExtensionOptions();
            foreach (var extension in extensionOptions)
            {
                extensions.Add(extension.Key, extension.Value);
            }
            if (extensionOptions.Count != 0)
            {
                payload.Add("extensions", extensions);
            }

            var concurrency = GetConcurrencyOptions();
            payload.Add("concurrency", concurrency);
            return payload;
        }

        private Dictionary<string, JObject> GetExtensionOptions()
        {
            Dictionary<string, JObject> result = new Dictionary<string, JObject>();
            foreach (IExtensionOptionsProvider extensionOptionsProvider in _extensionOptionsProviders)
            {
                var options = extensionOptionsProvider.GetOptions();
                if (typeof(IOptionsFormatter).IsAssignableFrom(options.GetType()))
                {
                    var optionsFormatter = (IOptionsFormatter)options;
                    string sectionName = extensionOptionsProvider?.ExtensionInfo?.ConfigurationSectionName?.CamelCaseString();
                    result.Add(sectionName, JObject.Parse(optionsFormatter.Format()).ToCamelCase());
                }
            }

            return result;
        }

        private JObject GetConcurrencyOptions()
        {
            var concurrencyOptions = _concurrencyOptions.CurrentValue;
            return JObject.FromObject(concurrencyOptions, _serializer);
        }
    }
}
