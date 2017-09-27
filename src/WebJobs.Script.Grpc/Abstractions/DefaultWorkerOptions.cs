// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public class DefaultWorkerOptions
    {
        /// <summary>
        /// The path to the worker
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// The debugging port
        /// </summary>
        public string Debug { get; set; } = string.Empty;

        public bool TryGetDebugPort(out int result) => int.TryParse(Debug, out result);
    }
}
