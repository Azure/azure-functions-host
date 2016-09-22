// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class FunctionStatus
    {
        /// <summary>
        /// Gets or sets the collection of initialization errors for the function.
        /// </summary>
        [JsonProperty(PropertyName = "errors", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Collection<string> Errors { get; set; }

        [JsonProperty(PropertyName = "metadata", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public FunctionMetadata Metadata { get; set; }
    }
}