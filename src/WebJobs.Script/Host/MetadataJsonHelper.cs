// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal static class MetadataJsonHelper
    {
        /// <summary>
        /// Sanitizes the values of top-level properties in the specified <see cref="JObject"/>
        /// whose names match any in the provided collection, using case-insensitive comparison.
        /// The original property casing is preserved.
        /// </summary>
        /// <param name="jsonObject">The <see cref="JObject"/> to sanitize.</param>
        /// <param name="propertyNames">A collection of top-level property names to sanitize.</param>
        /// <returns>
        /// A <see cref="JObject"/> with the specified properties' values sanitized if found.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="jsonObject"/> or <paramref name="propertyNames"/> is <c>null</c>.
        /// </exception>
        public static JObject CreateJObjectWithSanitizedPropertyValue(JObject jsonObject, ImmutableHashSet<string> propertyNames)
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
                    var valueToSanitize = prop.Value.Type == JTokenType.String ? (string)prop.Value : prop.Value.ToString();
                    jsonObject[prop.Name] = Sanitizer.Sanitize(valueToSanitize);
                }
            }

            return jsonObject;
        }

        /// <summary>
        /// Parses the input JSON string into a <see cref="JObject"/> and sanitizes the values of top-level properties
        /// whose names match any in the provided collection, using case-insensitive comparison.
        /// The original property casing is preserved.
        /// </summary>
        /// <param name="json">The JSON string to parse and sanitize.</param>
        /// <param name="propertyNames">A collection of top-level property names to sanitize.</param>
        /// <returns>
        /// A <see cref="JObject"/> representing the parsed JSON, with the specified properties' values sanitized if found.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="json"/> is <c>null</c> or empty.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="propertyNames"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="JsonReaderException">
        /// Thrown if <paramref name="json"/> is not a valid JSON string.
        /// </exception>
        public static JObject CreateJObjectWithSanitizedPropertyValue(string json, ImmutableHashSet<string> propertyNames)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Input JSON cannot be null or empty.", nameof(json));
            }

            ArgumentNullException.ThrowIfNull(propertyNames, nameof(propertyNames));

            var jsonObject = JObject.Parse(json);

            return CreateJObjectWithSanitizedPropertyValue(jsonObject, propertyNames);
        }
    }
}
