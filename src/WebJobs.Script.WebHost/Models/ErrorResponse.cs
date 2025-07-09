// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// Represents an error response.
    /// See https://github.com/Azure/azure-resource-manager-rpc/blob/master/v1.0/common-api-details.md#error-response-content.
    /// </summary>
    /// <param name="Code">
    /// The error code. This is NOT the HTTP status code.
    /// Unlocalized string which can be used to programmatically identify the error.
    /// The code should be Pascal-cased, and should serve to uniquely identify a particular class of error,
    /// for example "BadArgument".
    /// </param>
    /// <param name="Message">
    /// The error message. Describes the error in detail and provides debugging information.
    /// If Accept-Language is set in the request, it must be localized to that language.
    /// </param>]
    public record ErrorResponse(
        [property: JsonProperty("code")][property: JsonPropertyName("code")] string Code,
        [property: JsonProperty("message")][property: JsonPropertyName("message")] string Message)
    {
        /// <summary>
        /// Gets the target of the particular error. For example, the name of the property in error.
        /// </summary>
        [JsonProperty("target")]
        [JsonPropertyName("target")]
        public string Target { get; init; }

        /// <summary>
        /// Gets the details of this error.
        /// </summary>
        [JsonProperty("details")]
        [JsonPropertyName("details")]
        public IEnumerable<ErrorResponse> Details { get; init; } = [];

        /// <summary>
        /// Gets the additional information for this error.
        /// </summary>
        [JsonProperty("additionalInfo")]
        [JsonPropertyName("additionalInfo")]
        public IEnumerable<ErrorAdditionalInfo> AdditionalInfo { get; init; } = [];

        public static ErrorResponse BadArgument(string message, string target = null)
        {
            return new("BadArgument", message) { Target = target };
        }
    }

    /// <summary>
    /// Represents additional information for an error.
    /// </summary>
    /// <param name="Type">The type of additional information.</param>
    /// <param name="Info">The additional error information.</param>
    public record ErrorAdditionalInfo(
        [property: JsonProperty("type")][property: JsonPropertyName("type")] string Type,
        [property: JsonProperty("info")][property: JsonPropertyName("info")] object Info);
}