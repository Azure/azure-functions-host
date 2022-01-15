// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class ExtensionOptionsProvider
    {
        private readonly List<Type> _extensionOptionTypes;
        private readonly IServiceProvider _serviceProvider;

        public ExtensionOptionsProvider(IServiceProvider serviceProvider, IServiceCollection services)
        {
            _serviceProvider = serviceProvider;

            // this will give us a list of all extension option factories, and from that
            // we can get extension option types
            // TODO: this is just one example of a way to do this. May be a better way to get this info
            var extensionOptions = services.Where(p => p.ServiceType.IsGenericType && p.ServiceType.GetGenericTypeDefinition() == typeof(IConfigureOptions<>) && p.ImplementationFactory != null && p.ImplementationFactory.Target.ToString().StartsWith(typeof(WebJobsExtensionBuilderExtensions).FullName)).ToList();
            _extensionOptionTypes = extensionOptions.Select(p => p.ServiceType.GetGenericArguments()[0]).ToList();
        }

        public Dictionary<string, JObject> GetExtensionOptions()
        {
            // for each of the extension options types identified, create a bound instance
            // and format to JObject
            Dictionary<string, JObject> result = new Dictionary<string, JObject>();
            foreach (Type optionsType in _extensionOptionTypes)
            {
                // create the IOptions<T> type
                Type optionsWrapperType = typeof(IOptions<>).MakeGenericType(optionsType);
                object optionsWrapper = _serviceProvider.GetService(optionsWrapperType);
                PropertyInfo valueProperty = optionsWrapperType.GetProperty("Value");

                // create the instance - this will cause configuration binding
                object options = valueProperty.GetValue(optionsWrapper);

                // format extension name based on convention from options name
                // TODO: format this properly
                int idx = optionsType.Name.LastIndexOf("Options");
                string name = optionsType.Name.Substring(0, 1).ToLower() + optionsType.Name.Substring(1, idx - 1);

                // convert to JObject
                result.Add(name, FormatOptions(options));
            }

            return result;
        }

        private JObject FormatOptions(object options)
        {
            // convert to JObject
            // TODO: implement custom serialization or contract handler to
            // reduce to only simple types
            JObject jo = JObject.FromObject(options);
            return jo;
        }
    }
}
