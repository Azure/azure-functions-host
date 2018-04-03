// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Abstractions
{
    public class DefaultWorkerOptions
    {
        /// <summary>
        /// Gets or sets the path to the worker
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the debugging address
        /// </summary>
        public string Debug { get; set; } = string.Empty;
    }
}
