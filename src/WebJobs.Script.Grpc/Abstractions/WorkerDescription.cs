// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Abstractions
{
    public class WorkerDescription
    {
        /// <summary>
        /// Gets or sets the name of the supported language. This is the same name as the IConfiguration section for the worker.
        /// </summary>
        [JsonProperty(PropertyName = "language", Required = Required.Always)]
        public string Language { get; set; }

        /// <summary>
        /// Gets or sets the supported file extension type. Functions are registered with workers based on extension.
        /// </summary>
        [JsonProperty(PropertyName = "extension", Required = Required.Always)]
        public string Extension { get; set; }

        /// <summary>
        /// Gets or sets the default executable path.
        /// </summary>
        [JsonProperty(PropertyName = "defaultExecutablePath", Required = Required.Always)]
        public string DefaultExecutablePath { get; set; }

        /// <summary>
        /// Gets or sets the default path to the worker (relative to the bin/workers/{language} directory)
        /// </summary>
        [JsonProperty(PropertyName = "defaultWorkerPath", Required = Required.Always)]
        public string DefaultWorkerPath { get; set; }

        /// <summary>
        /// Gets or sets the default path to the worker (relative to the bin/workers/{language} directory)
        /// </summary>
        [JsonProperty(PropertyName = "arguments")]
        public List<string> Arguments { get; set; }
    }
}
