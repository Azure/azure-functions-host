// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Abstractions
{
    public class WorkerDescription
    {
        /// <summary>
        /// Gets or sets the name of the supported language. This is the same name as the IConfiguration section for the worker.
        /// </summary>
        [JsonProperty(PropertyName = "language")]
        public string Language { get; set; }

        /// <summary>
        /// Gets or sets the supported file extension type. Functions are registered with workers based on extension.
        /// </summary>
        [JsonProperty(PropertyName = "extensions")]
        public List<string> Extensions { get; set; }

        /// <summary>
        /// Gets or sets the default executable path.
        /// </summary>
        [JsonProperty(PropertyName = "defaultExecutablePath")]
        public string DefaultExecutablePath { get; set; }

        /// <summary>
        /// Gets or sets the default path to the worker (relative to the bin/workers/{language} directory)
        /// </summary>
        [JsonProperty(PropertyName = "defaultWorkerPath")]
        public string DefaultWorkerPath { get; set; }

        /// <summary>
        /// Gets or sets the default base directory for the worker
        /// </summary>
        [JsonProperty(PropertyName = "workerDirectory")]
        public string WorkerDirectory { get; set; }

        /// <summary>
        /// Gets or sets the default path to the worker (relative to the bin/workers/{language} directory)
        /// </summary>
        [JsonProperty(PropertyName = "arguments")]
        public List<string> Arguments { get; set; }

        public string GetWorkerPath()
        {
            if (string.IsNullOrEmpty(DefaultWorkerPath))
            {
                return null;
            }
            else
            {
                return Path.Combine(WorkerDirectory, DefaultWorkerPath);
            }
        }
    }
}
