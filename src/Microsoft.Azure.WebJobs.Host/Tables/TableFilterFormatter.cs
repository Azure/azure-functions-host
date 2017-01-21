// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal static class TableFilterFormatter
    {
        public static string Format(BindingTemplate template, IReadOnlyDictionary<string, object> bindingData)
        {
            if (!template.ParameterNames.Any())
            {
                return template.Pattern;
            }

            if (template.ParameterNames.Count() == 1)
            {
                // Special case where the entire filter expression
                // is a single parameter. We let this go through as is
                string parameterName = template.ParameterNames.Single();
                if (template.Pattern == $"{{{parameterName}}}")
                {
                    return template.Bind(bindingData);
                }
            }

            // each distinct parameter can occur one or more times in the template
            // so group by parameter name
            var parameterGroups = template.ParameterNames.GroupBy(p => p);

            // for each parameter, classify it as a string literal or other
            // and perform value validation
            var convertedBindingData = BindingDataPathHelper.ConvertParameters(bindingData);
            foreach (var parameterGroup in parameterGroups)
            {
                // to classify as a string literal, ALL occurrences in the template
                // must be string literals (e.g. of the form '{p}')
                // note that this will also capture OData expressions of the form
                // datetime'{p}', guid'{p}', X'{p}' which is fine, because single quotes
                // aren't valid for those values anyways.
                bool isStringLiteral = true;
                string parameterName = parameterGroup.Key;
                string stringParameterFormat = $"'{{{parameterName}}}'";
                int count = 0, idx = 0; 
                while (idx >= 0 && idx < template.Pattern.Length && count++ < parameterGroup.Count())
                {
                    idx = template.Pattern.IndexOf(stringParameterFormat, idx, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0)
                    {
                        isStringLiteral = false;
                        break;
                    }
                    idx++;
                }

                // validate and format the value based on its classification
                string value = null;
                if (convertedBindingData.TryGetValue(parameterName, out value))
                {
                    if (isStringLiteral)
                    {
                        convertedBindingData[parameterName] = value.Replace("'", "''");
                    }
                    else if (!TryValidateNonStringLiteral(value))
                    {
                        throw new InvalidOperationException($"An invalid parameter value was specified for filter parameter '{parameterName}'.");
                    }
                }

                // perform any OData specific formatting on the values
                object originalValue;
                if (bindingData.TryGetValue(parameterName, out originalValue))
                {
                    if (originalValue is DateTime)
                    {
                        // OData DateTime literals should be ISO 8601 formatted (e.g. 2009-03-18T04:25:03Z)
                        convertedBindingData[parameterName] = ((DateTime)originalValue).ToUniversalTime().ToString("o");
                    }
                    else if (originalValue is DateTimeOffset)
                    {
                        convertedBindingData[parameterName] = ((DateTimeOffset)originalValue).UtcDateTime.ToString("o");
                    }
                }
            }

            return template.Bind(convertedBindingData);
        }

        internal static bool TryValidateNonStringLiteral(string value)
        {
            // value must be one of the odata supported non string literal types:
            // bool, int, long, double
            bool boolValue;
            long longValue;
            double doubleValue;
            if (bool.TryParse(value, out boolValue) ||
                long.TryParse(value, out longValue) ||
                double.TryParse(value, out doubleValue))
            {
                return true;
            }

            return false;
        }
    }
}
