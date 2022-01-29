// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class SyncTriggerOptionProvider : ISyncTriggerOptionProvider
    {
        private readonly List<Type> _extensionOptionTypes;
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, string> _irregularNamingExtensions = new Dictionary<string, string>()
        {
            // the extension section name is taken from the prefix of the Option class name. e.g. KafkaOptions -> kafka.
            // However, some extensions doesn't follow the convention. Replacing the prefix with correct name.
            { "eventHub", "eventHubs" },
            { "queues", "queue" },
            { "blobs", "blob" }
        };

        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
            {
                ContractResolver = new ExtensionsOptionContractResolver(),
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include,
                Formatting = Formatting.Indented
            };

        private readonly JsonSerializerSettings _concurrencySettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };

        public SyncTriggerOptionProvider(IServiceProvider serviceProvider, IServiceCollection services)
        {
            _serviceProvider = serviceProvider;
            // TODO : The following code might have better ways, however, I can't find it until now.
            var extensionOptions = services.Where(p => p.ServiceType.IsGenericType && p.ServiceType.GetGenericTypeDefinition() == typeof(IConfigureOptions<>) && p.ImplementationFactory != null && p.ImplementationFactory.Target.ToString().StartsWith(typeof(WebJobsExtensionBuilderExtensions).FullName)).ToList();
            _extensionOptionTypes = extensionOptions.Select(p => p.ServiceType.GetGenericArguments()[0]).ToList();
        }

        public Dictionary<string, JObject> GetExtensionOptions()
        {
            var configuration = _serviceProvider.GetService<IConfiguration>();
            // for each of the extension options types identified, create a bound instance
            // and format to JObject
            Dictionary<string, JObject> result = new Dictionary<string, JObject>();
            foreach (Type optionsType in _extensionOptionTypes)
            {
                int idx = optionsType.Name.LastIndexOf("Options");
                string name = optionsType.Name.Substring(0, 1).ToLower() + optionsType.Name.Substring(1, idx - 1);
                if (_irregularNamingExtensions.ContainsKey(name))
                {
                    name = _irregularNamingExtensions[name];
                }
                var options = Activator.CreateInstance(optionsType);
                var key = ConfigurationSectionNames.JobHost + ConfigurationPath.KeyDelimiter
                     + "extensions" + ConfigurationPath.KeyDelimiter + name;
                configuration.Bind(key, options);
                // convert to JObject
                result.Add(name, FormatOptions(options));
            }

            return result;
        }

        public JObject GetConcurrencyOption()
        {
            var configuration = _serviceProvider.GetService<IConfiguration>();
            var concurrencyOption = new ConcurrencyOptions();
            var key = ConfigurationSectionNames.JobHost + ConfigurationPath.KeyDelimiter
                    + "concurrency";
            configuration.Bind(key, concurrencyOption);
            var json = JsonConvert.SerializeObject(concurrencyOption, _concurrencySettings);
            return JObject.Parse(json);
        }

        private JObject FormatOptions(object options)
        {
            return JObject.Parse(JsonConvert.SerializeObject(options, _settings));
        }
    }
}
