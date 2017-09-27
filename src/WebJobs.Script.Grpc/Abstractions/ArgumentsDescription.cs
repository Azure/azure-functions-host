// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public class ArgumentsDescription
    {
        /// <summary>
        /// The path to the executable (java, node, etc).
        /// </summary>
        public string ExecutablePath { get; set; }

        /// <summary>
        /// Arguments to be passed to the executable. Optional.
        /// </summary>
        public List<string> ExecutableArguments { get; set; } = new List<string>();

        /// <summary>
        /// The path to the worker file, i.e. nodejsWorker.js.
        /// </summary>
        public string WorkerPath { get; set; }

        /// <summary>
        /// Arguments to be passed to the worker. Optional.
        /// </summary>
        public List<string> WorkerArguments { get; set; } = new List<string>();
    }
}
