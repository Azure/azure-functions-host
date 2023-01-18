// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class WorkerProcessInfo
    {
        /// <summary>
        /// Gets or sets the worker process id.
        /// </summary>
        [JsonProperty(PropertyName = "processId", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ProcessId { get; set; }

        /// <summary>
        /// Gets or sets the name of the worker process executable.
        /// </summary>
        [JsonProperty(PropertyName = "executableName", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ExecutableName { get; set; }

        /// <summary>
        /// Gets or sets the debug engine string assosiated with the worker process.
        /// </summary>
        [JsonProperty(PropertyName = "debugEngine", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string DebugEngine { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the worker process is eligible to be opened in browser.
        /// </summary>
        [JsonProperty(PropertyName = "isEligibleForOpenInBrowser", DefaultValueHandling = DefaultValueHandling.Include)]
        public bool IsEligibleForOpenInBrowser { get; set; }
    }
}
