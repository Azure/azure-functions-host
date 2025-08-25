// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class MetadataJsonHelper
    {
        /// <summary>
        /// Sanitizes the values of top-level properties in the specified <see cref="JObject"/>
        /// whose names match any in the provided collection, using case-insensitive comparison.
        /// The original property casing is preserved.
        /// <strong>Note:</strong> This method mutates the input <see cref="JObject"/> only if one or more property values are sanitized.
        /// </summary>
        /// <param name="jsonObject">The <see cref="JObject"/> to sanitize. This object may be modified in place.</param>
        /// <param name="propertyNames">A collection of top-level property names to sanitize.</param>
        /// <returns>
        /// The modified <see cref="JObject"/> with the specified properties' values sanitized if found.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="jsonObject"/> or <paramref name="propertyNames"/> is <c>null</c>.
        /// </exception>
        public static JObject SanitizeProperties(JObject jsonObject, ImmutableHashSet<string> propertyNames)
        {
            ArgumentNullException.ThrowIfNull(jsonObject, nameof(jsonObject));
            ArgumentNullException.ThrowIfNull(propertyNames, nameof(propertyNames));

            if (propertyNames.Count == 0)
            {
                return jsonObject;
            }

            foreach (var prop in jsonObject.Properties())
            {
                if (propertyNames.Contains(prop.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (prop.Value.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    var valueToSanitize = prop.Value.Type == JTokenType.String ? (string)prop.Value : prop.Value.ToString();
                    jsonObject[prop.Name] = Sanitizer.Sanitize(valueToSanitize);
                }
            }

            return jsonObject;
        }

        /// <summary>
        /// Parses the input JSON string into a <see cref="JObject"/> and sanitizes the values of top-level properties
        /// whose names match any in the provided collection, using case-insensitive comparison.
        /// The original property casing is preserved. Allows customization of JSON date parsing behavior.
        /// </summary>
        /// <param name="json">The JSON string to parse and sanitize.</param>
        /// <param name="propertyNames">A collection of top-level property names to sanitize. Case-insensitive matching is used.</param>
        /// <param name="dateParseHandling">
        /// Specifies how date strings should be parsed. Defaults to <see cref="DateParseHandling.None"/> to avoid automatic date conversion.
        /// </param>
        /// <returns>
        /// A <see cref="JObject"/> representing the parsed JSON, with the specified properties' values sanitized if found.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="json"/> is <c>null</c>, empty, or whitespace.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="propertyNames"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="JsonReaderException">
        /// Thrown if <paramref name="json"/> is not a valid JSON string.
        /// </exception>
        public static JObject CreateJObjectWithSanitizedPropertyValue(string json, ImmutableHashSet<string> propertyNames, DateParseHandling dateParseHandling = DateParseHandling.None)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Input JSON cannot be null or empty.", nameof(json));
            }

            ArgumentNullException.ThrowIfNull(propertyNames, nameof(propertyNames));

            using var stringReader = new StringReader(json);
            using var jsonReader = new JsonTextReader(stringReader)
            {
                DateParseHandling = dateParseHandling
            };

            var jsonObject = JObject.Load(jsonReader);

            return SanitizeProperties(jsonObject, propertyNames);
        }
    }
}
