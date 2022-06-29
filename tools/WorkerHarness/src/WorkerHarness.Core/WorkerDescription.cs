// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    /// <summary>
    /// WorkerDescription encapsulates information about a language worker
    /// such as the language type, the executable path for that worker,
    /// the worker binary file location and the worker directory
    /// </summary>
    public class WorkerDescription
    {
        public string? Language { get; set; }

        public string? DefaultExecutablePath { get; set; }

        public string? DefaultWorkerPath { get; set; }

        public string? WorkerDirectory { get; set; }

    }
}
