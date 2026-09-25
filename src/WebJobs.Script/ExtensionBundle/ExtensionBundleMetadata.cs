// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Azure.WebJobs.Script.Conditions;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    /// <summary>
    /// Shape of bundle.json. Only the fields the host actually reads are modelled here.
    /// </summary>
    internal sealed class ExtensionBundleMetadata
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// Optional list of conditions that must all evaluate to true for the bundle to load.
        /// Missing/null/empty → bundle loads unconditionally (backward compat).
        /// </summary>
        [JsonPropertyName("requirements")]
        public IList<ConditionDescriptor> Requirements { get; set; }
    }
}
