// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ExtensionBundle
    {
        /// <summary>
        /// Gets or sets the id of the extension bundle
        /// </summary>
        [JsonProperty(PropertyName = "id", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the version of the extension bundle
        /// </summary>
        [JsonProperty(PropertyName = "version")]
        public string Version { get; set; }
    }
}