// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// Describes the runtime reported by a running language worker.
    /// </summary>
    public class LanguageWorkerStatus
    {
        /// <summary>
        /// Gets or sets the runtime name reported by the worker, for example <c>node</c>.
        /// </summary>
        [JsonProperty(PropertyName = "runtimeName", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string RuntimeName { get; set; }

        /// <summary>
        /// Gets or sets the runtime version reported by the worker, for example <c>22.11.0</c>.
        /// </summary>
        [JsonProperty(PropertyName = "runtimeVersion", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string RuntimeVersion { get; set; }

        /// <summary>
        /// Gets or sets the language worker version reported by the worker.
        /// </summary>
        [JsonProperty(PropertyName = "workerVersion", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string WorkerVersion { get; set; }
    }
}
