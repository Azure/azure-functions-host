// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Provides constants related to JSON serialization options used.
    /// </summary>
    public static class JsonSerializerOptionsProvider
    {
        /// <summary>
        /// Shared Json serializer with the following settings:
        /// - AllowTrailingCommas: true
        /// - PropertyNamingPolicy: CamelCase
        /// - DefaultIgnoreCondition: WhenWritingNull.
        /// </summary>
        public static readonly JsonSerializerOptions Options = CreateJsonOptions();

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            options.Converters.Add(new JsonStringEnumConverter());

            return options;
        }
    }
}
