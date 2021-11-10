// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    internal class ExternalConfigurationStartupValidator
    {
        private readonly IConfiguration _config;
        private readonly IFunctionMetadataManager _metadataManager;
        private readonly DefaultNameResolver _nameResolver;

        public ExternalConfigurationStartupValidator(IConfiguration config, IFunctionMetadataManager metadataManager)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
            _nameResolver = new DefaultNameResolver(config);
        }

        /// <summary>
        /// Validates the current configuration against the original configuration. If any values for a trigger
        /// do not match, they are returned via the return value.
        /// </summary>
        /// <param name="originalConfig">The original configuration</param>
        /// <returns>A dictionary mapping function name to a list of the invalid values for that function.</returns>
        public IDictionary<string, IEnumerable<string>> Validate(IConfigurationRoot originalConfig)
        {
            if (originalConfig == null)
            {
                throw new ArgumentNullException(nameof(originalConfig));
            }

            INameResolver originalNameResolver = new DefaultNameResolver(originalConfig);
            IDictionary<string, IEnumerable<string>> invalidValues = new Dictionary<string, IEnumerable<string>>();

            var functions = _metadataManager.GetFunctionMetadata();

            foreach (var function in functions)
            {
                // Only a single trigger per function is supported. For our purposes here we just take
                // the first. If multiple are defined, that error will be handled on indexing.
                var trigger = function.Bindings.FirstOrDefault(b => b.IsTrigger);
                if (trigger == null)
                {
                    continue;
                }

                IList<string> invalidValuesForFunction = new List<string>();

                // make sure none of the resolved values have changed for the trigger.
                foreach (KeyValuePair<string, JToken> property in trigger.Raw)
                {
                    string lookup = property.Value?.ToString();

                    if (lookup != null)
                    {
                        string originalValue = originalConfig[lookup];
                        string newValue = _config[lookup];
                        if (originalValue != newValue)
                        {
                            invalidValuesForFunction.Add(lookup);
                        }
                        else
                        {
                            // It may be a binding expression like "%lookup%"
                            originalNameResolver.TryResolveWholeString(lookup, out originalValue);
                            _nameResolver.TryResolveWholeString(lookup, out newValue);

                            if (originalValue != newValue)
                            {
                                invalidValuesForFunction.Add(lookup);
                            }
                        }
                    }
                }

                if (invalidValuesForFunction.Any())
                {
                    invalidValues[function.Name] = invalidValuesForFunction;
                }
            }

            return invalidValues;
        }
    }
}
